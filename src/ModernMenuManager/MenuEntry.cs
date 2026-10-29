using System;
using System.Collections.Generic;
using System.Linq;

namespace ModernMenuManager
{
    enum EntrySource
    {
        Package,   // Declared by an installed (or sparse) app package
        BuiltIn,   // Known Windows command that does not come from a package manifest
        Other,     // Found only in the block list, so it can be restored
    }

    sealed class MenuEntry
    {
        public Guid Clsid;
        public EntrySource Source;
        public string AppName;
        public string PackageFullName;
        public string ComDisplayName;
        public string VerbId;
        public volatile string ProbedTitle;
        public string FixedTitle;
        public readonly SortedSet<string> ItemTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool BlockedUser;
        public bool BlockedMachine;

        public bool IsHidden => BlockedUser || BlockedMachine;

        public string Title
        {
            get
            {
                var t = FixedTitle ?? ProbedTitle ?? ComDisplayName ?? VerbId ?? BlockList.Format(Clsid);
                // Drop menu accelerator markers ("&Open" -> "Open", "&&" -> "&").
                return t.Replace("&&", "\0").Replace("&", "").Replace("\0", "&");
            }
        }

        public string ItemTypesText => ItemTypes.Count == 0 ? "" : string.Join("、", ItemTypes.Select(DescribeItemType).Distinct());

        static string DescribeItemType(string type)
        {
            switch (type.ToLowerInvariant())
            {
                case "*": return "所有檔案";
                case "directory": return "資料夾";
                case "directory\\background": return "資料夾空白處";
                case "drive": return "磁碟";
                case "folder": return "資料夾";
                default: return type;
            }
        }
    }
}
