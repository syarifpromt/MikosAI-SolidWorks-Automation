using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MikosAI.Core.Threading;

namespace MikosAI.Addin
{
    /// <summary>
    /// Implementasi nyata <see cref="IMainThreadDispatcher"/>.
    ///
    /// API SolidWorks TIDAK thread-safe dan harus dipanggil dari thread STA utama
    /// yang diberikan SolidWorks. Dalam arsitektur kita ada tiga sumber
    /// panggilan konkuren: thread HttpListener dari McpHttpServer, thread pembaca
    /// pipe dari ProviderHost, dan callback WebView2 dari UI.
    ///
    /// Caranya: pada <c>ConnectToSW</c> kita membuat satu <see cref="Control"/>
    /// WinForms. Control itu terikat ke thread yang membuatnya, jadi
    /// <c>Control.BeginInvoke</c> menjadi kanal marshalling yang andal ke thread
    /// utama SolidWorks.
    ///
    /// Control sengaja TIDAK pernah ditampilkan. Ia hanya perlu punya handle.
    ///
    /// Aturan yang ditegakkan lewat review: tidak ada satu pun pemanggilan tipe
    /// SolidWorks.Interop.* di luar delegate yang dijalankan dispatcher ini.
    /// Lihat CONTRIBUTING.md aturan 1.
    /// </summary>
    internal sealed class MainThreadDispatcher : IMainThreadDispatcher, IDisposable
    {
        private readonly Control _marshaller;
        private readonly int _mainThreadId;
        private bool _disposed;

        /// <summary>
        /// HARUS dipanggil dari thread utama SolidWorks, yaitu dari dalam
        /// <c>ConnectToSW</c>. Dipanggil dari tempat lain, seluruh jaminan
        /// dispatcher ini batal.
        /// </summary>
        internal MainThreadDispatcher()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            _marshaller = new Control();

            // Memaksa pembuatan handle. Tanpa handle, BeginInvoke melempar
            // InvalidOperationException - dan kita ingin itu terjadi SEKARANG,
            // di tempat yang jelas, bukan nanti saat ada request MCP masuk.
            _ = _marshaller.Handle;
        }

        internal int MainThreadId => _mainThreadId;

        public bool IsOnMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
        {
            if (work is null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            if (_disposed)
            {
                return Task.FromException<T>(new ObjectDisposedException(nameof(MainThreadDispatcher)));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            // Sudah di thread utama: jalankan langsung. Lewat BeginInvoke dari
            // thread utama akan menunggu pump pesan berikutnya, dan kalau
            // pemanggil lalu menunggu hasilnya, itu deadlock.
            if (IsOnMainThread)
            {
                try
                {
                    return Task.FromResult(work());
                }
                catch (Exception exception)
                {
                    return Task.FromException<T>(exception);
                }
            }

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            }

            try
            {
                _marshaller.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            completion.TrySetCanceled(cancellationToken);
                            return;
                        }

                        completion.TrySetResult(work());
                    }
                    catch (Exception exception)
                    {
                        // Exception dari API SolidWorks diteruskan ke pemanggil,
                        // tidak dibiarkan lolos ke pump pesan - exception yang
                        // lolos di sana menjatuhkan SolidWorks.
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        registration.Dispose();
                    }
                }));
            }
            catch (Exception exception)
            {
                registration.Dispose();
                completion.TrySetException(exception);
            }

            return completion.Task;
        }

        public Task InvokeAsync(Action work, CancellationToken cancellationToken = default)
        {
            if (work is null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            return InvokeAsync<object?>(
                () =>
                {
                    work();
                    return null;
                },
                cancellationToken);
        }

        /// <summary>
        /// Memastikan pemanggil berada di thread utama. Dipakai di dalam kode
        /// yang menyentuh API SolidWorks sebagai jaring pengaman.
        ///
        /// Gagal keras itu disengaja: satu exception yang jelas saat development
        /// jauh lebih baik daripada crash acak di tangan pengguna.
        /// </summary>
        internal void AssertOnMainThread(string memberName)
        {
            if (!IsOnMainThread)
            {
                throw new MainThreadViolationException(memberName);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _marshaller.Dispose();
            }
            catch (Exception)
            {
                // Tidak ada yang bisa dilakukan saat menutup.
            }
        }
    }
}
