﻿using ICities;
using NodeMarkup.Manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NodeMarkup.UI;
using System.Globalization;
using ColossalFramework.Globalization;
using ColossalFramework;
using ColossalFramework.UI;
using ColossalFramework.PlatformServices;
using NodeMarkup.Utils;
using NodeMarkup.Tools;
using UnityEngine.SceneManagement;
using ModsCommon;
using ModsCommon.Utilities;
using ModsCommon.UI;

namespace NodeMarkup
{
    public class Mod : IUserMod
    {
        public static string StableURL { get; } = "https://steamcommunity.com/sharedfiles/filedetails/?id=2140418403";
        public static string BetaURL { get; } = "https://steamcommunity.com/sharedfiles/filedetails/?id=2159934925";
        public static string DiscordURL { get; } = "https://discord.gg/QRYq8m2";
        public static string ReportBugUrl { get; } = "https://github.com/MacSergey/NodeMarkup/issues/new?assignees=&labels=NEW+ISSUE&template=bug_report.md";
        public static string WikiUrl { get; } = "https://github.com/MacSergey/NodeMarkup/wiki";
        public static string TroubleshootingUrl { get; } = "https://github.com/MacSergey/NodeMarkup/wiki/Troubleshooting";

        public static Version Version => Assembly.GetExecutingAssembly().GetName().Version;
        public static Version VersionBuild => Version.Build();

        public static Logger Logger { get; } = new Logger(nameof(NodeMarkup));
        public static string StaticName { get; } = "Intersection Marking Tool";
#if DEBUG
        public static string StaticFullName => $"{StaticName} {Version.GetString()} [BETA]";
#else
        public static string StaticFullName => $"{StaticName} {Version.GetString()}";
#endif

        public static List<Version> Versions { get; } = new List<Version>
        {
            new Version("1.5"),
            new Version("1.4.1"),
            new Version("1.4"),
            new Version("1.3"),
            new Version("1.2.1"),
            new Version("1.2"),
            new Version("1.1"),
            new Version("1.0")
        };

        public string Name => StaticFullName;
#if DEBUG
        public static bool IsBeta => true;
        public string Description => Localize.Mod_DescriptionBeta;
#else
        public static bool IsBeta => false;
        public override string Description => Localize.Mod_Description;
#endif

        protected static CultureInfo Culture
        {
            get
            {
                var locale = string.IsNullOrEmpty(Settings.Locale.value) ? SingletonLite<LocaleManager>.instance.language : Settings.Locale.value;
                if (locale == "zh")
                    locale = "zh-cn";

                return new CultureInfo(locale);
            }
        }

        public void OnEnabled()
        {
            LoadingManager.instance.m_introLoaded += LoadedError;
            Logger.Debug($"Version {Version}");
            Logger.Debug($"{nameof(Mod)}.{nameof(OnEnabled)}");
            Patcher.Patch();
        }
        public void OnDisabled()
        {
            LoadingManager.instance.m_introLoaded -= LoadedError;
            Logger.Debug($"{nameof(Mod)}.{nameof(OnDisabled)}");
            Patcher.Unpatch();
            NodeMarkupTool.Remove();

            LocaleManager.eventLocaleChanged -= LocaleChanged;
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            LocaleManager.eventLocaleChanged -= LocaleChanged;
            LocaleChanged();
            LocaleManager.eventLocaleChanged += LocaleChanged;

            Logger.Debug($"{nameof(Mod)}.{nameof(OnSettingsUI)}");
            Settings.OnSettingsUI(helper);
        }

        public static void LocaleChanged()
        {
            Localize.Culture = Culture;
            Logger.Debug($"current cultute - {Localize.Culture?.Name ?? "null"}");
        }

        public static bool OpenTroubleshooting()
        {
            Utilities.OpenUrl(TroubleshootingUrl);
            return true;
        }
        public static bool GetStable()
        {
            Utilities.OpenUrl(StableURL);
            return true;
        }

        public static void LoadedError()
        {
            if (!Patcher.Success)
            {
                var messageBox = MessageBoxBase.ShowModal<ErrorLoadedMessageBox>();
                messageBox.MessageText = Localize.Mod_LoaledWithErrors;
            }
        }
    }
}
