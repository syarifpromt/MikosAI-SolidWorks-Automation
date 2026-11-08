using SolidWorks.Interop.sldworks;

namespace MikosAI.Addin
{
    internal static class BlockExtrusion
    {
        // Dipanggil di dalam writer yang sudah menegakkan dispatcher.
        // Tanda tangan dari refleksi PIA asli; test menangkap 23 argumen aktual.
        internal static Feature Create(IFeatureManager manager, double depthMetres)
            => manager.FeatureExtrusion2(
                Sd:true,Flip:false,Dir:false,T1:0,T2:0,D1:depthMetres,D2:0,
                Dchk1:false,Dchk2:false,Ddir1:false,Ddir2:false,Dang1:0,Dang2:0,
                OffsetReverse1:false,OffsetReverse2:false,TranslateSurface1:false,TranslateSurface2:false,
                Merge:true,UseFeatScope:true,UseAutoSelect:true,T0:0,StartOffset:0,FlipStartOffset:false);
    }
}
