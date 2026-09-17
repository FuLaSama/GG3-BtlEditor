/*
 * FrontNamedExport.cs
 *
 * 旧名字。关卡 JSON 导出已改到 StageJson（普通字段名 / 字段号，不再包 t/f）。
 */
using BtlCore.Front;

namespace BtlCore.Fb
{
    public static class FrontNamedExport
    {
        public static string ToJson(BtlFrontDocument doc, FbsSchema schema) =>
            StageJson.ToNamed(doc, schema);
    }
}
