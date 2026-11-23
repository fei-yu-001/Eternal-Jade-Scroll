using System.IO;
using UnityEditor;
using UnityEngine;

namespace TwelveJade.Editor
{
    // 美术贴图导入守卫：Resources/Art 下所有贴图强制一套"可切片、可预期"的导入设置。
    //
    // 背景（2026-10-01）：所有立绘的 .meta 带着 nPOTScale: 1，Unity 把 427×720 的图
    // 按 2 的幂重采样/填充，纸偶按 v 比例切 0.22/0.76/1.0 时腿那一段取到空白区，
    // 表现就是"腿没了"。手改 .meta 不可靠（文件写对了 Unity 也可能沿用旧导入产物），
    // 改用 AssetPostprocessor 在导入时强制——文件里怎么写都不影响最终设置。
    //
    // 关键项：
    //   nPOTScale = None    非常规尺寸不再被缩放/填充，uvRect 比例与源图严格一致；
    //   不压缩、不开 mipmap  避免运行期尺寸与格式再变形，也省显存；
    //   Clamp + Bilinear    切片边缘不串色，缩放不糊。
    public sealed class ArtImportGuard : AssetPostprocessor
    {
        const string ArtRoot = "Assets/Resources/Art/";
        // 世界分块贴图：整幅铺满屏幕的不透明地形色块，与立绘的诉求正好相反。
        const string WorldChunksRoot = "Assets/Resources/Art/World/Chunks/";

        static bool IsArt(string path) => path.StartsWith(ArtRoot) &&
                                         path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase);

        static bool IsWorldChunk(string path) => path.StartsWith(WorldChunksRoot) &&
                                          path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase);

        void OnPreprocessTexture()
        {
            if (!IsArt(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            // Eight 384px hero cells need their original 3072px width for clean frame sampling.
            bool heroWalk = assetPath.StartsWith(ArtRoot + "Characters/Qingshi/qingshi-", System.StringComparison.Ordinal) &&
                assetPath.EndsWith("-walk.png", System.StringComparison.OrdinalIgnoreCase);
            importer.maxTextureSize = heroWalk ? 4096 : 2048;

            if (IsWorldChunk(assetPath))
            {
                // 世界块例外（Gate C1-C）：64 张 1024² 若沿用立绘那套"不压缩 + 无 mipmap"，
                // 显存约 341MB；压缩 + mipmap 后约 45MB。块会被摄像机缩放平移，mipmap 必须开，
                // 否则跑图拉远时地面会闪噪。地面无 alpha，alphaIsTransparency 关掉省一次混合。
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Compressed;
                return;
            }

            // 立绘原规则（Items / NPC / Props）：不压缩、不开 mipmap、保留 alpha 语义。
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

        // 编辑器里手动执行"十二玉楼/修复美术导入设置"时走这里，批量重导一遍。
        [MenuItem("十二玉楼/修复美术导入设置")]
        public static void ReimportAll()
        {
            var files = Directory.GetFiles(Application.dataPath + "/Resources/Art", "*.png", SearchOption.AllDirectories);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var file in files)
                    AssetDatabase.ImportAsset("Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/'),
                        ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            Debug.Log("[ArtImportGuard] 已强制重导 " + files.Length + " 张美术贴图。");
        }
    }
}
