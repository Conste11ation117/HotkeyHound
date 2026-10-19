using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HotkeyHound.Core.Adapters
{
    public interface IConfigAdapter
    {
        string Software { get; }
        IEnumerable<DeclaredHotkey> Collect();
    }

    public abstract class AdapterBase : IConfigAdapter
    {
        public abstract string Software { get; }

        private readonly List<string> _log = new List<string>();

        public IEnumerable<string> Log { get { return _log; } }

        protected void Note(string message)
        {
            _log.Add("[" + Software + "] " + message);
        }

        protected void Warn(string message)
        {
            _log.Add(L10n.T("adapter.warnPrefix") + "[" + Software + "] " + message);
        }

        public abstract IEnumerable<DeclaredHotkey> Collect();

        /// <summary>把适配器的异常全部吞掉：一个坏配置不能拖垮整轮采集。</summary>
        protected static List<DeclaredHotkey> Safe(AdapterBase adapter, Func<List<DeclaredHotkey>> body)
        {
            try
            {
                return body();
            }
            catch (Exception ex)
            {
                adapter.Warn(L10n.F("adapter.collectFailed", ex.GetType().Name, ex.Message));
                return new List<DeclaredHotkey>();
            }
        }

        protected static DeclaredHotkey Make(string software, string module, string purpose, KeyCombo combo,
                                             string raw, string source, bool isDefault)
        {
            var d = new DeclaredHotkey();
            d.Software = software;
            d.Module = module;
            d.Purpose = purpose;
            d.Combo = combo;
            d.RawValue = raw;
            d.SourcePath = source;
            d.IsDefaultValue = isDefault;
            return d;
        }
    }

    internal static class AdapterUtil
    {
        public static string Expand(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            try { return Environment.ExpandEnvironmentVariables(path); }
            catch (Exception) { return path; }
        }

        public static string LocalAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        public static string RoamingAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }
        }

        public static string ExeName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            try
            {
                string name = Path.GetFileName(path.Trim());
                return string.IsNullOrEmpty(name) ? path.Trim() : name;
            }
            catch (Exception) { return path; }
        }

        /// <summary>纯文本配置文件（.ini/.cfg）按 UTF-8 读；失败时退回系统默认编码。</summary>
        public static string[] ReadLines(string path)
        {
            if (!File.Exists(path)) return new string[0];
            try
            {
                return File.ReadAllLines(path, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                try { return File.ReadAllLines(path); }
                catch (Exception) { return new string[0]; }
            }
        }
    }
}
