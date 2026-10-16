using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace HotkeyHound.Core
{
    /// <summary>
    /// 读取构建时内嵌进 exe 的帮助文档 HTML（docs/help.zh-CN.html、docs/help.en-US.html）。
    /// </summary>
    public static class HelpResources
    {
        public const string ZhResource = "help.zh-CN.html";
        public const string EnResource = "help.en-US.html";

        public static string ResourceName(string lang)
        {
            return L10n.Normalize(lang) == L10n.Chinese ? ZhResource : EnResource;
        }

        public static string FileName(string lang)
        {
            return (L10n.Normalize(lang) == L10n.Chinese ? "help.zh-CN" : "help.en-US") + ".html";
        }

        /// <summary>读取内嵌帮助 HTML；失败返回 null。</summary>
        public static string Read(string lang)
        {
            string name = ResourceName(lang);
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    using (var reader = new StreamReader(s, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception) { return null; }
        }
    }
}
