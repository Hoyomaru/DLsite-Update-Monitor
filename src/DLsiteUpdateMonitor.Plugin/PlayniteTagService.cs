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
            // without treating arbitrary user-created tags that share the prefix as plugin-owned.
            RemoveOwnTags(game);
            if (!enabled) return;

            switch (state)
            {
                case MonitoringState.PendingUpdateInfo:
                    AddTag(game, UpdateTag);
                    break;
                case MonitoringState.PendingFileChange:
                    AddTag(game, FileTag);
                    break;
                case MonitoringState.PendingUpdateAndFileChange:
                    AddTag(game, UpdateTag);
                    AddTag(game, FileTag);
                    break;
            }
        }

        private void AddTag(Game game, string tagName)
        {
            var tag = api.Database.Tags.FirstOrDefault(t => t.Name == tagName);
            if (tag == null)
            {
                tag = new Tag(tagName);
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
                return tag != null
                    && (string.Equals(tag.Name, UpdateTag, StringComparison.Ordinal)
                        || string.Equals(tag.Name, FileTag, StringComparison.Ordinal));
            }).ToList();

            foreach (var id in ownIds) game.TagIds.Remove(id);
        }
    }
}
