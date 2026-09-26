using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace DLsiteUpdateMonitor
{
    public partial class UpdateCenterView : UserControl
    {
        private readonly ObservableCollection<UpdateCenterItem> items = new ObservableCollection<UpdateCenterItem>();
        private readonly ICollectionView collectionView;

        public event EventHandler<UpdateCenterActionEventArgs> ActionRequested;
        public event EventHandler CloseRequested;

        public UpdateCenterView(IEnumerable<UpdateCenterItem> initialItems)
        {
            InitializeComponent();
            foreach (var item in initialItems ?? Enumerable.Empty<UpdateCenterItem>())
            {
                items.Add(item);
            }

            ItemsGrid.ItemsSource = items;
            collectionView = CollectionViewSource.GetDefaultView(items);
            collectionView.Filter = FilterItem;
            FilterComboBox.SelectedIndex = 0;
            UpdateSummary();
            RefreshSelectionState();
        }

        public void SetItems(IEnumerable<UpdateCenterItem> updatedItems)
        {
            ItemsGrid.SelectedItems.Clear();
            items.Clear();
            foreach (var item in updatedItems ?? Enumerable.Empty<UpdateCenterItem>())
            {
                items.Add(item);
            }

            collectionView.Refresh();
            UpdateSummary();
            RefreshSelectionState();
        }

        public void SetStatusMessage(string message)
        {
            StatusMessageText.Text = message ?? string.Empty;
            StatusMessageText.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private bool FilterItem(object value)
        {
            var item = value as UpdateCenterItem;
            if (item == null) return false;

            var filter = (FilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Attention";
            var included = filter == "All"
                || (filter == "Attention" && item.NeedsAttention)
                || (filter == "Update" && item.HasUpdateInfoChange)
                || (filter == "File" && item.HasFileChange)
                || (filter == "Error" && item.HasHealthIssue)
                || (filter == "Clean" && !item.NeedsAttention);

            if (!included) return false;

            var query = (SearchTextBox.Text ?? string.Empty).Trim();
            if (query.Length == 0) return true;

            return Contains(item.GameName, query)
                || Contains(item.ProductId, query)
                || Contains(item.ChangeText, query)
                || Contains(item.HealthText, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void UpdateSummary()
        {
            TrackedCountText.Text = items.Count.ToString();
            AttentionCountText.Text = items.Count(item => item.NeedsAttention).ToString();
            UpdateCountText.Text = items.Count(item => item.HasUpdateInfoChange).ToString();
            FileCountText.Text = items.Count(item => item.HasFileChange).ToString();
            ErrorCountText.Text = items.Count(item => item.HasHealthIssue).ToString();
        }

        private List<UpdateCenterItem> SelectedItems()
        {
            return ItemsGrid.SelectedItems.OfType<UpdateCenterItem>().ToList();
        }

        private void RefreshSelectionState()
        {
            var selected = SelectedItems();
            var one = selected.Count == 1;
            var hasSelection = selected.Count > 0;
            var hasPending = selected.Any(item => item.HasPendingChange);

            RecheckButton.IsEnabled = hasSelection;
            AppliedButton.IsEnabled = hasPending;
            IgnoreButton.IsEnabled = hasPending;
            DetailsButton.IsEnabled = one;
            OpenPageButton.IsEnabled = one;

            if (!hasSelection)
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                DetailEmptyText.Visibility = Visibility.Visible;
                DetailEmptyText.Text = "左の一覧からゲームを選択してください。";
                return;
            }

            if (!one)
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                DetailEmptyText.Visibility = Visibility.Visible;
                DetailEmptyText.Text = selected.Count + "件選択中です。下のボタンからまとめて処理できます。";
                return;
            }

            var item = selected[0];
            DetailEmptyText.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            DetailGameNameText.Text = item.GameName;
            DetailChangeText.Text = "変更: " + item.ChangeText;
            DetailHealthText.Text = "確認状態: " + item.HealthText;
            DetailProductIdText.Text = item.ProductId;
            DetailUpdateInfoText.Text = item.UpdateInfoText;
            DetailFileSizeText.Text = item.FileSizeText;
            DetailLastCheckText.Text = item.LastCheckText;

            var hasError = !string.IsNullOrWhiteSpace(item.ErrorText);
            DetailErrorPanel.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
            DetailErrorText.Text = item.ErrorText ?? string.Empty;
        }

        private void RaiseAction(UpdateCenterAction action)
        {
            var selected = SelectedItems();
            if (selected.Count == 0) return;

            ActionRequested?.Invoke(this, new UpdateCenterActionEventArgs(
                action,
                selected.Select(item => item.GameId).ToList()));
        }

        private void FilterChanged(object sender, SelectionChangedEventArgs e)
        {
            collectionView?.Refresh();
        }

        private void SearchChanged(object sender, TextChangedEventArgs e)
        {
            collectionView?.Refresh();
        }

        private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshSelectionState();
        }

        private void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedItems().Count == 1)
            {
                RaiseAction(UpdateCenterAction.ShowDetails);
            }
        }

        private void RecheckButton_Click(object sender, RoutedEventArgs e) => RaiseAction(UpdateCenterAction.Recheck);
        private void AppliedButton_Click(object sender, RoutedEventArgs e) => RaiseAction(UpdateCenterAction.MarkApplied);
        private void IgnoreButton_Click(object sender, RoutedEventArgs e) => RaiseAction(UpdateCenterAction.Ignore);
        private void DetailsButton_Click(object sender, RoutedEventArgs e) => RaiseAction(UpdateCenterAction.ShowDetails);
        private void OpenPageButton_Click(object sender, RoutedEventArgs e) => RaiseAction(UpdateCenterAction.OpenPage);

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public enum UpdateCenterAction
    {
        Recheck,
        MarkApplied,
        Ignore,
        ShowDetails,
        OpenPage
    }

    public sealed class UpdateCenterActionEventArgs : EventArgs
    {
        public UpdateCenterAction Action { get; }
        public IReadOnlyList<Guid> GameIds { get; }

        public UpdateCenterActionEventArgs(UpdateCenterAction action, IReadOnlyList<Guid> gameIds)
        {
            Action = action;
            GameIds = gameIds ?? Array.Empty<Guid>();
        }
    }

    public sealed class UpdateCenterItem
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; }
        public string ProductId { get; set; }
        public string ChangeText { get; set; }
        public string HealthText { get; set; }
        public string LastCheckText { get; set; }
        public string UpdateInfoText { get; set; }
        public string FileSizeText { get; set; }
        public string ErrorText { get; set; }
        public bool HasPendingChange { get; set; }
        public bool HasUpdateInfoChange { get; set; }
        public bool HasFileChange { get; set; }
        public bool HasHealthIssue { get; set; }
        public bool NeedsAttention => HasPendingChange || HasHealthIssue;
    }
}
