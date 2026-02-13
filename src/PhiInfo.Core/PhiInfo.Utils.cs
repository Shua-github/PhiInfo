using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PhiInfo.Core
{
    public partial class PhiInfo
    {
        private AssetTypeValueField? FindMonoBehaviour(
        AssetsFile file,
        string name)
        {
            foreach (var info in file.AssetInfos)
            {
                if (info.TypeId != (int)AssetClassID.MonoBehaviour)
                    continue;

                var baseField = GetBaseField(file, info, false);

                var scriptField = baseField["m_Script"];
                if (scriptField == null)
                    continue;

                var msId = scriptField["m_PathID"].AsLong;
                if (msId == 0)
                    continue;

                var monoInfo = ggmInst.GetAssetInfo(msId);
                if (monoInfo == null)
                    continue;

                var msBase = GetBaseField(ggmInst, monoInfo, false);
                var msName = msBase["m_Name"]?.AsString;

                if (msName == name)
                {
                    return GetBaseField(file, info, true);
                }
            }

            return null;
        }
    }
}