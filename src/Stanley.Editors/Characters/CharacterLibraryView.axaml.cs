using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Stanley.Editors;

public partial class CharacterLibraryView : UserControl
{
	private const double DragThresholdPx = 6;

	private CharacterItem? _pressedItem;
	private PointerPressedEventArgs? _pressArgs;
	private Point _pressPoint;
	private bool _dragging;

	// The character the last click landed on, which a second click of the same burst puts on the page.
	private CharacterItem? _clickedItem;
	// The character a double-click just put on the page (a third click of the burst leaves it there),
	// and the press that did it - the handler sees each press on the way down and again on the way up.
	private CharacterItem? _placedItem;
	private PointerPressedEventArgs? _placedBy;

	public CharacterLibraryView()
	{
		InitializeComponent();

		// handledEventsToo: the list box handles presses itself (to select); a drag starts from the same press.
		CharacterList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
		CharacterList.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
		CharacterList.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
		CharacterList.ContextRequested += OnContextRequested;
		CharacterList.KeyDown += OnListKeyDown;
		// Picking a tile adds the character; the gallery then goes away, as a menu would. Posted:
		// a button runs its command after its Click event, and a closed flyout's buttons have lost
		// the DataContext their commands are bound through.
		CharacterGalleryPanel.AddHandler(Button.ClickEvent, (_, e) =>
		{
			if (e.Source is Button { Classes: var classes } && classes.Contains("characterChoice"))
				Dispatcher.UIThread.Post(() => AddCharacterButton.Flyout?.Hide());
		});
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		if (_revealing is { } previous)
			previous.CharacterRevealed -= OnCharacterRevealed;
		_revealing = ViewModel;
		if (_revealing != null)
			_revealing.CharacterRevealed += OnCharacterRevealed;
		base.OnDataContextChanged(e);
	}

	private CharacterLibraryViewModel? _revealing;

	/// <summary>A character just added from the gallery (or one the comic already had): scrolled into view, not opened.</summary>
	private void OnCharacterRevealed(CharacterItem item) => CharacterList.ScrollIntoView(item);

	private void OnGalleryOpening(object? sender, EventArgs e) => ViewModel?.RefreshGallery();

	private CharacterLibraryViewModel? ViewModel => DataContext as CharacterLibraryViewModel;

	/// <summary>Exposed for headless UI tests.</summary>
	public ListBox List => CharacterList;

	private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(CharacterList).Properties.IsLeftButtonPressed || ReferenceEquals(e, _placedBy))
			return;
		// A press inside the rename box is text editing (placing the caret, dragging out a
		// selection) - leave it to the TextBox instead of tracking it as a drag/click on the item.
		if (e.Source is Visual pressed && pressed.GetSelfAndVisualAncestors().OfType<TextBox>().Any())
			return;
		// A press anywhere else while a name is being edited closes that editor first, the way
		// losing focus would - so clicking another character (or empty space) gets you out of it.
		CommitActiveRename();
		// A press on a row's own Edit button is that button's click, not a click or a drag on the
		// row - so two quick clicks on it can't add up to a double-click that places the character.
		if (e.Source is Visual onButton && onButton.GetSelfAndVisualAncestors().OfType<Button>().Any())
		{
			_clickedItem = null;
			return;
		}
		var item = ItemFrom(e.Source);
		// A double-click puts the character on the page. Its first click only picked the row
		// (it doesn't open the editor), so placing it needs nothing undone. Handled here, on
		// the way down, so the list doesn't take the focus the page gets.
		if (e.ClickCount >= 2 && item != null && (ReferenceEquals(item, _clickedItem) || ReferenceEquals(item, _placedItem)) && ViewModel is { } vm)
		{
			_placedBy = e;
			_pressedItem = null;
			_pressArgs = null;
			_clickedItem = null;
			if (!ReferenceEquals(item, _placedItem))
			{
				_placedItem = item;
				vm.PlaceOnPage(item);
			}
			e.Handled = true;
			return;
		}
		_placedItem = null;
		_pressedItem = item;
		_pressArgs = e;
		_pressPoint = e.GetPosition(CharacterList);
		_dragging = false;
	}

	private async void OnListPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_pressedItem is not { } item || _pressArgs is not { } press || _dragging)
			return;
		var position = e.GetPosition(CharacterList);
		if (Math.Abs(position.X - _pressPoint.X) < DragThresholdPx && Math.Abs(position.Y - _pressPoint.Y) < DragThresholdPx)
			return;

		_dragging = true;
		_clickedItem = null;
		var data = new DataTransfer();
		data.Add(DataTransferItem.Create(CharacterDrag.Format, item.Id.Value));
		try
		{
			await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
		}
		finally
		{
			_dragging = false;
			_pressedItem = null;
			_pressArgs = null;
		}
	}

	private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		// A click - press and release on the same character without dragging - only picks it,
		// which the list box already did on press; a second click of the burst places it. Opening
		// the editor takes a deliberate step (the row's pencil, Enter, right-click > Edit body), as
		// a stray click on the list shouldn't throw you out of the page you were working on.
		if (!_dragging && _pressedItem is { } item && ItemFrom(e.Source) == item)
			_clickedItem = item;
		_pressedItem = null;
		_pressArgs = null;
	}

	private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
	{
		if (ViewModel is not { } vm || ItemFrom(e.Source) is not { } item)
			return;
		CommitActiveRename();

		var menu = new ContextMenu
		{
			ItemsSource = (Control[])
			[
				Item("Rename", () => item.IsEditingName = true),
				Item("Edit body", () => vm.EditCharacterCommand.Execute(item), gesture: "Enter"),
				Item("Place on page", () => vm.PlaceOnPageCommand.Execute(item)),
				new Separator(),
				.. MyAssetsItems(vm, item),
				Item("Duplicate", () => vm.DuplicateCharacterCommand.Execute(item)),
				Item(item.Usage > 0 ? "Delete (remove it from its panels first)" : "Delete", () => vm.DeleteCharacterCommand.Execute(item), item.Usage == 0, "Delete"),
			]
		};
		menu.Open(CharacterList);
		e.Handled = true;
	}

	private void OnListKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter && ViewModel is { } opener && CharacterList.SelectedItem is CharacterItem selected)
		{
			opener.EditCharacterCommand.Execute(selected);
			e.Handled = true;
			return;
		}
		if (ViewModel is not { } vm || (CharacterList.SelectedItem as CharacterItem ?? vm.Current) is not { } current)
			return;
		if (e.Key == Key.Delete && vm.DeleteCharacterCommand.CanExecute(current))
		{
			vm.DeleteCharacterCommand.Execute(current);
			e.Handled = true;
		}
	}

	/// <summary>Keep in My Assets, Save to My Assets once it's changed here, or a quiet "Kept in My Assets" while it matches.</summary>
	private static Control[] MyAssetsItems(CharacterLibraryViewModel vm, CharacterItem item)
	{
		if (!vm.HasMyAssets)
			return [];
		var keep = vm.KeptStateOf(item) switch
		{
			KeptState.NotKept => Item("Keep in My Assets", () => vm.KeepInMyAssetsCommand.Execute(item)),
			KeptState.ChangedHere => Item("Save to My Assets", () => vm.KeepInMyAssetsCommand.Execute(item)),
			_ => Item("Kept in My Assets", () => { }, enabled: false)
		};
		keep.Icon = new PathIcon { Data = (Avalonia.Media.Geometry)Application.Current!.FindResource("StarIcon")!, Width = 14, Height = 14 };
		return [keep, new Separator()];
	}

	private static MenuItem Item(string header, Action action, bool enabled = true, string? gesture = null)
	{
		var item = new MenuItem { Header = header, IsEnabled = enabled, InputGesture = gesture is null ? null : KeyGesture.Parse(gesture) };
		item.Click += (_, _) => action();
		return item;
	}

	private static CharacterItem? ItemFrom(object? source) =>
		(source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as CharacterItem;
	private void TextboxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (
			e.Property.Name is nameof(TextBox.IsVisible) &&
			e.OldValue is false && 
			sender is TextBox { IsVisible: true } textBox
		)
		{
			textBox.Focus();
			textBox.SelectAll();
		}
	}
	private void InputElement_OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (sender is not TextBox { DataContext: CharacterItem item } textBox)
			return;
		if (e.Key == Key.Enter)
		{
			// Commits directly instead of moving focus off the box: with a single character (no
			// next control to move focus to) that move used to fail silently, leaving the box open.
			item.Name = textBox.Text ?? item.Name;
			CharacterList.Focus();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			// Cancels: whatever was typed is discarded and the name is left as it was.
			textBox.Text = item.Name;
			item.IsEditingName = false;
			CharacterList.Focus();
			e.Handled = true;
		}
	}

	/// <summary>Commits whichever character's name is currently being edited, if any - the same outcome losing focus gives, for callers that close the editor another way.</summary>
	private void CommitActiveRename()
	{
		var textBox = CharacterList.GetVisualDescendants().OfType<TextBox>()
			.FirstOrDefault(t => t.DataContext is CharacterItem { IsEditingName: true });
		if (textBox?.DataContext is CharacterItem item)
			item.Name = textBox.Text ?? item.Name;
	}
}