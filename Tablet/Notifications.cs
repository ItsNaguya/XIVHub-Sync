using System;
using System.Collections.Generic;
using System.Linq;

namespace XIVHubCompanion.Tablet
{
    public sealed class Notice
    {
        public string Key = "";
        public string Source = "";
        public string Title = "";
        public string Body = "";
        public string? App;
        public DateTime At = DateTime.Now;
        public bool Seen;
    }

    /// <summary>The tablet's notification shade: gathering alarms, retainers back, events starting, resets.</summary>
    public static class Notifications
    {
        private static readonly List<Notice> Items = new();
        private static readonly object Lock = new();
        public const int Max = 40;

        public static int Unseen { get { lock (Lock) return Items.Count(n => !n.Seen); } }
        public static List<Notice> All() { lock (Lock) return Items.ToList(); }

        /// <summary>Adds a notice; the same key replaces the older one (one entry per node, event, ...).</summary>
        public static void Post(string key, string source, string title, string body, string? app = null)
        {
            lock (Lock)
            {
                Items.RemoveAll(n => n.Key == key);
                Items.Insert(0, new Notice { Key = key, Source = source, Title = title, Body = body, App = app });
                if (Items.Count > Max) Items.RemoveRange(Max, Items.Count - Max);
            }
        }

        public static void MarkAllSeen() { lock (Lock) foreach (var n in Items) n.Seen = true; }
        public static void Dismiss(Notice n) { lock (Lock) Items.Remove(n); }
        public static void Clear() { lock (Lock) Items.Clear(); }
    }
}
