using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace DLsiteUpdateMonitor
{
    public partial class UpdateCenterView : UserControl
    {
        private readonly DLsiteUpdateMonitorPlugin plugin;
        private readonly List<UpdateCenterItem> items = new List<UpdateCenterItem>();
        private ICollectionView view;

        internal UpdateCenterView(DLsiteUpdateMonitorPlugin plugin)
        {
            // XAML controls can raise SelectionChanged/TextChanged while InitializeComponent()
            // is still building the visual tree. Assign the dependency first so those handlers
            // never observe a null plugin reference during construction.
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            InitializeComponent();
            RefreshRows();
        }

        private void RefreshRows()
        {
            items.Clear();
            items.AddRange(plugin.GetUpdateCenterItems());
            ItemsGrid.ItemsSource = null;
            ItemsGrid.ItemsSource = items;
            view = CollectionViewSource.GetDefaultView(ItemsGrid.ItemsSource);
            if (view != null) view.Filter = FilterItem;
            RefreshSummary();
            RefreshSelectionState();
        }

        private bool FilterItem(object value)
        {
            var item = value as UpdateCenterItem;
            if (item == null) return false;

            var query = (SearchBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(query)
                && (item.GameName ?? string.Empty).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0
                && (item.ProductId ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            var selected = FilterCombo.SelectedItem as ComboBoxItem;
            var tag = selected?.Tag as string ?? "All";
            switch (tag)
            {
                case "Pending": return item.HasPendingChange;
                case "Attention": return item.NeedsAttention;
                case "Clean": return item.FilterBucket == "Clean";
                case "Uninitialized": return item.FilterBucket == "Uninitialized";
                default: return true;
            }
        }

        private void RefreshSummary()
        {
            var pending = items.Count(x => x.HasPendingChange);
            var attention = items.Count(x => x.NeedsAttention);
            var clean = items.Count(x => x.FilterBucket == "Clean");
            SummaryText.Text = $"追跡中 {items.Count}件 · 未処理 {pending}件 · エラー/要確認 {attention}件 · 変更なし {clean}件\n{plugin.GetAutomaticCheckSummary()}";
            if (view != null) VisibleCountText.Text = $"表示 {view.Cast<object>().Count()}件";
        }

        private void RefreshSelectionState()
        {
            var count = ItemsGrid?.SelectedItems?.Count ?? 0;
            if (SelectionText != null)
            {
                SelectionText.Text = count == 0 ? "選択なし" : $"{count}件選択中";
            }

            var hasSelection = count > 0;
            if (CheckSelectedButton != null) CheckSelectedButton.IsEnabled = hasSelection;
            if (AppliedButton != null) AppliedButton.IsEnabled = hasSelection;
            if (IgnoreButton != null) IgnoreButton.IsEnabled = hasSelection;

            var single = count == 1;
            if (DetailsButton != null) DetailsButton.IsEnabled = single;
            if (OpenPageButton != null) OpenPageButton.IsEnabled = single;
        }

        private List<Guid> SelectedGameIds()
        {
            return ItemsGrid.SelectedItems.Cast<UpdateCenterItem>().Select(x => x.GameId).Distinct().ToList();
        }

        private void WithSelection(Action<List<Guid>> action, bool single = false)
        {
            var ids = SelectedGameIds();
            if (ids.Count == 0)
            {
                plugin.ShowInfo("操作するゲームを一覧から選択してください。");
                return;
            }
            if (single && ids.Count != 1)
            {
                plugin.ShowInfo("この操作は1ゲームだけ選択して実行してください。");
                return;
            }
            action(ids);
            RefreshRows();
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            view?.Refresh();
            RefreshSummary();
            RefreshSelectionState();
        }

        private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshSelectionState();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRows();

        private void CheckSelectedButton_Click(object sender, RoutedEventArgs e)
            => WithSelection(ids => plugin.CheckGamesFromUpdateCenter(ids));

        private void RecheckAttentionButton_Click(object sender, RoutedEventArgs e)
        {
            var ids = items.Where(x => x.NeedsAttention).Select(x => x.GameId).Distinct().ToList();
            if (ids.Count == 0)
            {
                plugin.ShowInfo("再確認が必要なゲームはありません。");
                return;
            }
            plugin.CheckGamesFromUpdateCenter(ids);
            RefreshRows();
        }

        private void AppliedButton_Click(object sender, RoutedEventArgs e)
            => WithSelection(ids => plugin.AcknowledgeFromUpdateCenter(ids, false));

        private void IgnoreButton_Click(object sender, RoutedEventArgs e)
            => WithSelection(ids => plugin.AcknowledgeFromUpdateCenter(ids, true));

        private void DetailsButton_Click(object sender, RoutedEventArgs e)
            => WithSelection(ids => plugin.ShowTrackingDetailsFromUpdateCenter(ids[0]), true);

        private void OpenPageButton_Click(object sender, RoutedEventArgs e)
            => WithSelection(ids => plugin.OpenDlsitePageFromUpdateCenter(ids[0]), true);

        private void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = ItemsGrid.SelectedItem as UpdateCenterItem;
            if (row != null) plugin.ShowTrackingDetailsFromUpdateCenter(row.GameId);
        }
    }
}
