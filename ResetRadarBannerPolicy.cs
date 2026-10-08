using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Globalization;

namespace CodexUsageOverlay
{
    internal sealed class ResetRadarBannerPolicy
    {
        private readonly string path;
        private BannerState state = new BannerState();
        private string pending;
        private DateTimeOffset? shownAt;

        internal ResetRadarBannerPolicy(string statePath)
        {
            path = statePath;
            try
            {
                if (File.Exists(path))
                {
                    BannerState loaded = new JavaScriptSerializer().Deserialize<BannerState>(File.ReadAllText(path));
                    if (loaded != null && loaded.Seen != null && loaded.Seen.Count <= 64) state = loaded;
                }
            }
            catch { } // Establish a silent baseline again if public reminder state is unreadable.
        }

        internal void Observe(ResetRadarData data, DateTimeOffset now)
        {
            if (data == null || !data.NetworkAvailable || data.IsFromCache ||
                data.Status == ResetRadarStatus.Loading || data.Status == ResetRadarStatus.Offline)
            { Dismiss(); return; }
            string key = ResetRadarDisplay.ShouldShow(data, now)
                ? ResumeJson.Hash(new[] { data.EvidencePostId, data.EventKind, data.SourceUrl, data.ScopeLabel,
                    Timestamp(data.AnnouncedAt), Timestamp(data.EffectiveAt), Timestamp(data.EffectiveUntil) })
                : null;
            if (!state.Initialized)
            {
                state.Initialized = true;
                if (key != null) state.Seen.Add(key);
                Save(); // First live result is a baseline, not a new announcement.
                return;
            }
            if (pending != key) Dismiss();
            if (key == null || state.Seen.Contains(key)) return;
            state.Seen.Add(key);
            if (state.Seen.Count > 64) state.Seen.RemoveAt(0);
            // Remember before showing so restart, refresh and manual close cannot replay it.
            if (Save()) { pending = key; shownAt = null; }
        }

        internal bool ShouldDisplay(DateTimeOffset now)
        {
            if (pending == null) return false;
            if (!shownAt.HasValue) shownAt = now;
            if (now < shownAt.Value.AddSeconds(10)) return true;
            Dismiss();
            return false;
        }

        internal void Dismiss() { pending = null; shownAt = null; }

        private static string Timestamp(DateTimeOffset? value)
        { return value.HasValue ? value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : ""; }

        private bool Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(state), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                return true;
            }
            catch { return false; }
        }

        public sealed class BannerState
        {
            public bool Initialized { get; set; }
            public List<string> Seen { get; set; }
            public BannerState() { Seen = new List<string>(); }
        }
    }
}
