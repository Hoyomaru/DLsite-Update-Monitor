using System;
using System.Collections.Generic;
using System.Linq;
using DLsiteUpdateMonitor.Core.Models;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace DLsiteUpdateMonitor
{
    public sealed class PlayniteTagService
    {
        private const string Prefix = "[DLsite更新] ";
        private const string UpdateTag = Prefix + "更新あり";
        private const string FileTag = Prefix + "配布物変更";
        private readonly IPlayniteAPI api;

        public PlayniteTagService(IPlayniteAPI api)
        {
            this.api = api;
        }

        public void Apply(Game game, MonitoringState state, bool enabled)
        {
            if (game == null) return;

            // Always remove our previous state first. This makes disabling tag integration reversible
            // instead of leaving stale [DLsite更新] tags behind.
            RemoveOwnTags(game);
            if (!enabled) return;
            string target = null;
            switch (state)
            {
                case MonitoringState.PendingUpdateInfo:
                case MonitoringState.PendingUpdateAndFileChange:
                    target = UpdateTag;
                    break;
                case MonitoringState.PendingFileChange:
                    target = FileTag;
                    break;
            }

            if (target == null) return;
            var tag = api.Database.Tags.FirstOrDefault(t => t.Name == target);
            if (tag == null)
            {
                tag = new Tag(target);
                api.Database.Tags.Add(tag);
            }
            if (game.TagIds == null) game.TagIds = new List<Guid>();
            if (!game.TagIds.Contains(tag.Id)) game.TagIds.Add(tag.Id);
        }

        public void RemoveOwnTags(Game game)
        {
            if (game?.TagIds == null) return;
            var ownIds = game.TagIds.Where(id =>
            {
                var tag = api.Database.Tags.Get(id);
                return tag != null && tag.Name.StartsWith(Prefix, StringComparison.Ordinal);
            }).ToList();

            foreach (var id in ownIds) game.TagIds.Remove(id);
        }
    }
}
