using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MikosAI.Core.Agents;

namespace MikosAI.Addin
{
    /// <summary>
    /// Mengelola provider AI.
    ///
    /// Menemukan `agy`, memeriksa keadaannya, mendaftarkan server MCP kita ke
    /// konfigurasinya, dan menjalankan permintaan.
    ///
    /// Tidak memakai ConPTY: bug stdout non-TTY yang mewajibkannya terbukti
    /// tidak ada di agy 1.4.11. Deteksi `provider_silent` tetap ada sebagai
    /// jaring pengaman. Lihat docs/integrasi-antigravity.md bagian 1.
    /// </summary>
    internal sealed class ProviderHost : IDisposable
    {
        private readonly HostDiagnostics _diagnostics;
        private readonly IAgentProvider? _provider;

        private ProviderProbe _probe = new ProviderProbe();
        private bool _disposed;

        internal ProviderHost(HostDiagnostics diagnostics, IAgentProvider? provider = null)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            // Adapter dibuat sendiri kalau agy ditemukan. Parameter provider
            // tetap ada supaya test dan diagnosis bisa memasang yang lain.
            if (provider is null)
            {
                var path = CariAgy();
                if (path is not null)
                {
                    provider = new AntigravityProvider(path, diagnostics);
                }
            }

            _provider = provider;
        }

        internal string DisplayName => _provider?.DisplayName ?? "Antigravity CLI";

        internal ProviderProbe Probe => _probe;

        /// <summary>
        /// Memeriksa keadaan provider. Tidak pernah melempar exception - add-in
        /// tetap berguna walaupun integrasi AI belum jalan.
        /// </summary>
        internal async Task PeriksaAsync(CancellationToken cancellationToken = default)
        {
            if (_provider is null)
            {
                // Belum ada adapter yang terpasang. Dideteksi lewat PATH saja,
                // supaya panel bisa membedakan "belum dipasang" dari "sudah
                // dipasang tapi integrasinya belum ada".
                _probe = PeriksaTanpaAdapter();
                return;
            }

            try
            {
                _probe = await _provider.ProbeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _diagnostics.AddError($"memeriksa provider {_provider.Id}", exception);
                _probe = new ProviderProbe
                {
                    State = ProviderState.NotInstalled,
                    Message = exception.Message,
                };
            }
        }

        /// <summary>
        /// Deteksi minimal tanpa adapter: apakah `agy` ada di PATH.
        ///
        /// Keadaannya TIDAK dinaikkan melewati Installed, karena menemukan entry
        /// di PATH tidak membuktikan apa pun soal autentikasi - dan menampilkan
        /// "siap" di sini akan jadi kebohongan yang baru ketahuan saat
        /// permintaan pertama gagal.
        /// </summary>
        private ProviderProbe PeriksaTanpaAdapter()
        {
            var path = CariAgy();

            if (path is null)
            {
                return new ProviderProbe
                {
                    State = ProviderState.NotInstalled,
                    Message = "agy.exe tidak ditemukan, baik di PATH maupun di "
                              + @"%USERPROFILE%\.gemini\bin.",
                };
            }

            return new ProviderProbe
            {
                State = ProviderState.Installed,
                ExecutablePath = path,
                Message = "Terpasang, tapi adapter provider belum diaktifkan di versi ini.",
            };
        }

        /// <summary>
        /// Mencari agy.exe.
        ///
        /// PATH SAJA TIDAK CUKUP. Installer Antigravity menaruh CLI di
        /// <c>%USERPROFILE%\.gemini\bin\agy.exe</c> dan hanya menambahkannya ke
        /// PATH kalau pengguna menjalankan <c>agy install</c>. Di mesin uji
        /// pertama kami, `agy` sudah terpasang DAN sudah login, tapi tidak ada
        /// di PATH - dan deteksi berbasis PATH melewatkannya sepenuhnya.
        ///
        /// Lebih buruk lagi: add-in berjalan di dalam SLDWORKS.exe, yang bisa
        /// mewarisi PATH berbeda dari terminal pengguna. Jadi lokasi bakunya
        /// diperiksa langsung.
        ///
        /// Urutan: variabel lingkungan override, lalu lokasi baku, lalu PATH.
        /// </summary>
        private static string? CariAgy()
        {
            try
            {
                var paksa = Environment.GetEnvironmentVariable("MIKOSAI_AGY_PATH");
                if (!string.IsNullOrWhiteSpace(paksa) && System.IO.File.Exists(paksa))
                {
                    return paksa;
                }
            }
            catch (Exception)
            {
                // Lanjut ke kandidat berikutnya.
            }

            try
            {
                var baku = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".gemini",
                    "bin",
                    "agy.exe");

                if (System.IO.File.Exists(baku))
                {
                    return baku;
                }
            }
            catch (Exception)
            {
                // Lanjut ke PATH.
            }

            return CariDiPath("agy.exe") ?? CariDiPath("agy");
        }

        private static string? CariDiPath(string namaBerkas)
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(path))
                {
                    return null;
                }

                foreach (var direktori in path!.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(direktori))
                    {
                        continue;
                    }

                    try
                    {
                        var kandidat = System.IO.Path.Combine(direktori.Trim(), namaBerkas);
                        if (System.IO.File.Exists(kandidat))
                        {
                            return kandidat;
                        }
                    }
                    catch (Exception)
                    {
                        // Entry PATH yang tidak valid - lanjut ke berikutnya.
                    }
                }
            }
            catch (Exception)
            {
                // PATH tidak bisa dibaca.
            }

            return null;
        }

        /// <summary>
        /// Menjalankan satu permintaan.
        ///
        /// Selama adapter belum terpasang, ini mengembalikan kegagalan yang
        /// MENJELASKAN keadaannya - bukan pura-pura bekerja, dan bukan error
        /// generik.
        /// </summary>
        internal async Task<AgentResult> JalankanAsync(
            string prompt,
            string workingDirectory,
            Action<string>? onDelta = null,
            CancellationToken cancellationToken = default)
        {
            if (!ProviderStateRules.MayAttemptRequest(_probe.State))
            {
                return new AgentResult
                {
                    Failure = new AgentFailure
                    {
                        Code = "provider_not_ready",
                        Message = ProviderStateRules.Describe(_probe.State, DisplayName),
                        NextAction = ProviderStateRules.NextAction(_probe.State, DisplayName),
                    },
                };
            }

            if (_provider is null)
            {
                return new AgentResult
                {
                    Failure = new AgentFailure
                    {
                        Code = "provider_adapter_missing",
                        Message =
                            $"{DisplayName} terpasang, tapi adapter-nya belum diaktifkan di versi ini. "
                            + "Delapan item verifikasi Antigravity harus dijawab lebih dulu - "
                            + "menulis integrasinya di atas asumsi sudah pernah membuat kami salah.",
                        NextAction = "Lihat docs/integrasi-antigravity.md bagian 7.",
                    },
                };
            }

            var permintaan = new AgentRequest
            {
                Prompt = prompt,
                WorkingDirectory = workingDirectory,
                PreferStreaming = onDelta is not null,
                BerkasIzin = SiapkanBerkasIzin(workingDirectory),
            };

            // ATURAN IZIN HARUS BENAR-BENAR SAMPAI KE agy.
            //
            // Ada DUA cara aturan izin gagal terkirim, dan keduanya berakhir
            // sama: agy menolak otomatis setiap tool MCP di mode headless, dan
            // yang terlihat pengguna hanyalah jawaban kosong.
            //
            //   1. berkasnya tidak bisa ditulis -> BerkasIzin null
            //   2. probe tidak menemukan --settings -> flagnya tidak dikirim
            //
            // Nomor 2 adalah yang benar-benar terjadi, dan sebabnya `agy --help`
            // menulis ke stderr sementara probe membaca stdout. Butuh berhari-
            // hari untuk menemukannya karena tidak ada yang mengeluh; semuanya
            // melapor "berhasil" lalu menampilkan kosong.
            //
            // Jadi keduanya dihentikan DI SINI, dengan sebab yang disebut. Lebih
            // baik menolak bekerja daripada bekerja sambil selalu gagal: jalur
            // yang terlihat jalan tapi tidak pernah memberi jawaban adalah yang
            // paling lama menyesatkan.
            var sebabIzinTidakTerkirim = SebabAturanIzinTidakTerkirim(permintaan);
            if (sebabIzinTidakTerkirim is not null)
            {
                _diagnostics.AddError(sebabIzinTidakTerkirim);

                return new AgentResult
                {
                    Failure = new AgentFailure
                    {
                        Code = "provider_permission_rule_undeliverable",
                        Message =
                            "Aturan izin MCP tidak bisa dikirim ke " + DisplayName + ", jadi "
                            + "permintaan tidak dijalankan: setiap tool SolidWorks akan "
                            + "ditolak otomatis dan jawabannya kosong.\n\n" + sebabIzinTidakTerkirim,
                        NextAction =
                            "Jalankan scripts\\addin.ps1 -Action Probe untuk melihat flag yang "
                            + "terdeteksi dari agy --help, lalu bandingkan dengan `agy --help` "
                            + "di terminal. Catatan: agy mencetak bantuannya ke stderr.",
                    },
                };
            }

            try
            {
                if (_provider is AntigravityProvider antigravity)
                {
                    var hasil = await antigravity
                        .KirimAsync(permintaan, _probe, onDelta, cancellationToken)
                        .ConfigureAwait(false);

                    // Satu permintaan yang benar-benar mengembalikan hasil
                    // terstruktur adalah SATU-SATUNYA bukti bahwa eksekusi
                    // bekerja. Keadaan dinaikkan hanya setelah itu terjadi.
                    if (hasil.IsSuccessful)
                    {
                        _probe.State = ProviderState.ExecutionVerified;
                    }

                    return hasil;
                }

                return new AgentResult
                {
                    Failure = new AgentFailure
                    {
                        Code = "provider_adapter_missing",
                        Message = $"Adapter untuk {DisplayName} belum ada.",
                    },
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menjalankan permintaan provider", exception);

                return new AgentResult
                {
                    Failure = new AgentFailure
                    {
                        Code = "provider_command_failed",
                        Message = exception.Message,
                    },
                };
            }
        }

        /// <summary>
        /// Mendaftarkan server MCP kita ke konfigurasi agy.
        ///
        /// Memakai jalur resmi `agy mcp add`, yang menurut dokumentasinya
        /// "Add or update" - jadi memanggilnya setiap sesi menimpa entri lama.
        /// Itu penting karena port kita ephemeral.
        ///
        /// Tidak pernah melempar exception: gagal mendaftar berarti AI tidak
        /// bisa memanggil tool CAD, tapi add-in tetap berguna.
        /// </summary>
        internal async Task<bool> DaftarkanMcpAsync(
            Uri endpoint,
            string token,
            CancellationToken cancellationToken = default)
        {
            if (_provider is not AntigravityProvider antigravity)
            {
                return false;
            }

            try
            {
                var hasil = await antigravity
                    .JalankanPerintahAsync(
                        AntigravityCommand.BuatArgumenDaftarMcp(endpoint, token),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!hasil)
                {
                    _diagnostics.AddError("agy mcp add gagal - AI tidak akan melihat tool CAD kami.");
                }

                McpTerdaftar = hasil;
                return hasil;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("mendaftarkan server MCP ke agy", exception);
                return false;
            }
        }

        /// <summary>
        /// Melepas pendaftaran. Dipanggil saat disconnect supaya tidak ada
        /// entri basi yang menunjuk port mati.
        /// </summary>
        internal async Task LepasMcpAsync(CancellationToken cancellationToken = default)
        {
            if (_provider is not AntigravityProvider antigravity || !McpTerdaftar)
            {
                return;
            }

            try
            {
                await antigravity
                    .JalankanPerintahAsync(AntigravityCommand.BuatArgumenHapusMcp(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("melepas pendaftaran MCP dari agy", exception);
            }
            finally
            {
                McpTerdaftar = false;
            }
        }

        internal bool McpTerdaftar { get; private set; }

        /// <summary>Ringkasan untuk panel UI.</summary>
        internal IReadOnlyDictionary<string, object?> Ringkasan()
            => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = _provider?.Id ?? "antigravity",
                ["displayName"] = DisplayName,
                ["state"] = _probe.State.ToString(),
                ["version"] = _probe.Version,
                ["connected"] = ProviderStateRules.MayBeShownAsConnected(_probe.State),
                ["mayAttempt"] = ProviderStateRules.MayAttemptRequest(_probe.State) && _provider is not null,
                ["mcpRegistered"] = McpTerdaftar,
                ["description"] = ProviderStateRules.Describe(_probe.State, DisplayName),
                ["nextAction"] = ProviderStateRules.NextAction(_probe.State, DisplayName)
                                 ?? (_provider is null && _probe.State == ProviderState.Installed
                                     ? "Adapter provider belum diaktifkan di versi ini."
                                     : null),
            };

        /// <summary>
        /// Menulis berkas setelan sesi untuk <c>--settings</c>, dan mengembalikan
        /// path-nya. null kalau gagal.
        ///
        /// Ditulis ulang setiap permintaan alih-alih sekali saat connect: isinya
        /// murah, dan berkas yang terhapus di antara permintaan tidak boleh
        /// mematikan chat secara diam-diam.
        ///
        /// TANPA BOM. Parser JSON agy menolak BOM dengan
        /// "invalid character '﻿' looking for beginning of value", dan itu
        /// kesalahan yang mudah terulang karena hampir semua penulis berkas di
        /// Windows memasangnya secara default.
        ///
        /// Isinya dari AntigravityCommand.IsiBerkasIzin supaya add-in dan
        /// scripts/mcp-roundtrip.ps1 tidak pernah menyimpang.
        /// </summary>
        /// <summary>
        /// Kenapa aturan izin MCP tidak akan sampai ke agy, kalau memang tidak.
        /// null berarti aturannya akan terkirim.
        ///
        /// Dipisah supaya alasannya satu tempat, dan supaya pesan ke pengguna
        /// menyebut sebab yang SPESIFIK alih-alih "izin gagal".
        /// </summary>
        private string? SebabAturanIzinTidakTerkirim(AgentRequest permintaan)
        {
            if (permintaan.BerkasIzin is null)
            {
                return "Berkas aturan izin tidak bisa ditulis ke folder kerja agent.";
            }

            if (_probe.DeteksiFlagGagal)
            {
                return
                    "Deteksi flag dari `agy --help` tidak menghasilkan apa pun, jadi tidak "
                    + "diketahui apakah --settings didukung. CLI tanpa flag apa pun tidak "
                    + "ada, jadi yang gagal adalah deteksinya.";
            }

            if (!_probe.Supports("--settings"))
            {
                return
                    "agy " + (_probe.Version ?? "(versi tak diketahui)") + " tidak "
                    + "mengiklankan --settings di --help, jadi aturan izin tidak punya "
                    + "jalan masuk ke sesi headless.";
            }

            return null;
        }

        private string? SiapkanBerkasIzin(string workingDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workingDirectory))
                {
                    return null;
                }

                System.IO.Directory.CreateDirectory(workingDirectory);

                var path = System.IO.Path.Combine(workingDirectory, "agy-settings.json");

                System.IO.File.WriteAllText(
                    path,
                    AntigravityCommand.IsiBerkasIzin(),
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                return path;
            }
            catch (Exception exception)
            {
                _diagnostics.AddError("menulis berkas izin agy", exception);
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
    }
}
