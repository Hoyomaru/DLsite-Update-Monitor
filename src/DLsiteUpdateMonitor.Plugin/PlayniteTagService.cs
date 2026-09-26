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
        private bool knownTagIdsLoaded;
        private Guid? updateTagId;
        private Guid? fileTagId;

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
            var tag = GetOrCreateTag(tagName);

            if (game.TagIds == null) game.TagIds = new List<Guid>();
            if (!game.TagIds.Contains(tag.Id)) game.TagIds.Add(tag.Id);
        }

        public void RemoveOwnTags(Game game)
        {
            if (game?.TagIds == null) return;
            EnsureKnownTagIds();

            // Resolve the two plugin-owned tag IDs once per service lifetime instead of looking up
            // every Game.TagId for every tracked game during a batch.
            if (updateTagId.HasValue) game.TagIds.Remove(updateTagId.Value);
            if (fileTagId.HasValue) game.TagIds.Remove(fileTagId.Value);
        }

        private Tag GetOrCreateTag(string tagName)
        {
            EnsureKnownTagIds();

            Guid? cachedId = string.Equals(tagName, UpdateTag, StringComparison.Ordinal)
                ? updateTagId
                : fileTagId;
            if (cachedId.HasValue)
            {
                var cached = api.Database.Tags.Get(cachedId.Value);
                if (cached != null && string.Equals(cached.Name, tagName, StringComparison.Ordinal))
                {
                    return cached;
                }

                // The tag was deleted/replaced while Playnite was running. Refresh just this name.
                if (string.Equals(tagName, UpdateTag, StringComparison.Ordinal)) updateTagId = null;
                else fileTagId = null;
            }

            var tag = api.Database.Tags.FirstOrDefault(t => string.Equals(t.Name, tagName, StringComparison.Ordinal));
            if (tag == null)
            {
                tag = new Tag(tagName);
                api.Database.Tags.Add(tag);
            }

            if (string.Equals(tagName, UpdateTag, StringComparison.Ordinal)) updateTagId = tag.Id;
            else fileTagId = tag.Id;
            return tag;
        }

        private void EnsureKnownTagIds()
        {
            if (knownTagIdsLoaded) return;

            // One scan is enough for both exact plugin-owned names. This keeps user-created tags
            // sharing the prefix out of the ownership boundary.
            foreach (var tag in api.Database.Tags)
            {
                if (tag == null) continue;
                if (string.Equals(tag.Name, UpdateTag, StringComparison.Ordinal))
                {
                    updateTagId = tag.Id;
                }
                else if (string.Equals(tag.Name, FileTag, StringComparison.Ordinal))
                {
                    fileTagId = tag.Id;
                }

                if (updateTagId.HasValue && fileTagId.HasValue) break;
            }

            knownTagIdsLoaded = true;
        }
    }
}
