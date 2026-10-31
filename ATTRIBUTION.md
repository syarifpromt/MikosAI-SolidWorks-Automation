# Atribusi

## solidworks-automation-skill

Arsitektur MikosAI dipengaruhi secara signifikan oleh
[wzyn20051216/solidworks-automation-skill](https://github.com/wzyn20051216/solidworks-automation-skill),
berlisensi MIT.

Konsep yang kami adopsi:

- **Capability ledger sebagai satu sumber kebenaran** dengan status `verified` / `pilot` /
  `reference_only` / `not_implemented`
- **"Tool return success bukan berarti task success"** dan verifikasi lewat baca-ulang model
- **Agregasi verifikasi** `BLOCKED > FAIL > WARN > PASS`, dengan status tidak dikenal turun
  ke `BLOCKED`
- **Pemisahan keputusan pemulihan dari eksekusi pemulihan**, beserta taksonomi klasifikasi
  error
- **State machine status provider** yang membedakan terpasang, terautentikasi, dan
  terverifikasi eksekusi
- **Gerbang penerimaan add-in host**, termasuk jebakan TLB dan `IDispatch` yang mereka
  dokumentasikan
- **DesignPlan sebagai artefak parametrik** yang bisa diedit, dipengaruhi subskill VibeCAD
  mereka

Analisis lengkap ada di [docs/bedah-repo-referensi.md](docs/bedah-repo-referensi.md).

MikosAI adalah implementasi clean-room dengan arsitektur berbeda — eksekusi C# in-process
dan MCP server di dalam add-in, bukan Python out-of-process dengan AI eksternal. Kalau ada
file yang di-port langsung, file itu akan dicatat di bawah beserta atribusi MIT-nya.

### File yang di-port langsung

Belum ada.

---

## Lisensi MIT dari karya di atas

```
MIT License

Copyright (c) 2026 SolidWorks Automation Skill Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Dependensi pihak ketiga

- **SolidWorks Primary Interop Assemblies** — dirujuk dari instalasi SolidWorks lokal,
  **tidak** di-vendor ke repo ini. Redistribusinya dibatasi lisensi Dassault Systèmes.
- **Microsoft WebView2 Runtime** — dependensi runtime, diinstal pengguna.
- **Antigravity CLI (`agy`)** — produk Google, diinstal dan diautentikasi pengguna secara
  terpisah. Kami tidak mendistribusikan atau menyertakan kredensial apa pun.

SolidWorks adalah merek dagang Dassault Systèmes SolidWorks Corporation. Proyek ini tidak
berafiliasi dengan, tidak didukung oleh, dan tidak disponsori oleh Dassault Systèmes atau
Google.
