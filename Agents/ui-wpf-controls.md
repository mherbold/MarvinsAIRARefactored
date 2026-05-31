# UI & WPF Controls

## Related Source Files
- `Controls/` — All custom `Maira*` WPF controls
- `Windows/` — All WPF windows
- `Pages/` — All WPF UserControl pages (hosted in `MainWindow`)
- `Converters/` — WPF value converters
- `Classes/TextBoxBehaviors.cs` — WPF text box helper behaviors
- `Classes/HelpService.cs` — Context-sensitive help
- `Classes/Misc.cs` — DPI helpers, version utilities
- `Artwork/` — Embedded PNG/ICO resources

---

## Always Use Custom Maira Controls

**Never use plain WPF controls when a Maira equivalent exists.** The custom controls enforce the app's visual style, support localization labels, and integrate with the context-switch system.

| Instead of… | Use… |
|---|---|
| `TextBox` | `controls:MairaTextBox` |
| `ComboBox` | `controls:MairaComboBox` |
| `Button` | `controls:MairaButton` |
| `CheckBox` / toggle | `controls:MairaSwitch` |
| `Slider` | `controls:MairaDualSlider` or `controls:MairaKnob` |
| `GroupBox` | `controls:MairaGroupBox` |
| `TabItem` | `controls:MairaTabItem` |

---

## MairaTextBox

A labeled text input.

| Property | Type | Notes |
|---|---|---|
| `Label` | `string` | Displayed above the input; bind to `Localization[Key]` |
| `Value` | `string` | Two-way bound to the data source |
| `IsNumericOnly` | `bool` | Restricts input to numeric characters |

```xml
<controls:MairaTextBox Label="{Binding DataContext.Localization[MyLabel], RelativeSource={RelativeSource AncestorType=UserControl}}"
						Value="{Binding MyProperty, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" />
```

**Key behaviors:**
- **Enter** commits the binding and moves focus to the next field automatically.
- Use `UpdateSourceTrigger=LostFocus` (not `PropertyChanged`) for numeric fields.
- Inside a `DataTemplate`, pass the data item via `Tag="{Binding}"` and use the `LostFocus` routed event to sync settings after edits.

---

## MairaComboBox

A labeled combo box.

| Property | Type | Notes |
|---|---|---|
| `Label` | `string` | Displayed above the control |
| `SelectedValue` | `object` | Two-way bound to the enum/value property |
| `ItemsSource` | `IEnumerable` | List of `KeyValuePair<TEnum, string>` items |
| `SelectionChanged` | event | Routed event fired on user selection |

**General pattern:**
```xml
<controls:MairaComboBox Label="{Binding Localization[MyLabel]}"
						 SelectedValue="{Binding Settings.MyEnumProperty, Mode=TwoWay}"
						 ItemsSource="{Binding MyOptionsProperty}" />
```

### Inside DataTemplates — Initialization Pattern

When inside a `DataTemplate` you **cannot** set `ItemsSource` in XAML. Use the `Loaded` event:

```xml
<controls:MairaComboBox Label="{Binding DataContext.Localization[MyLabel], RelativeSource={RelativeSource AncestorType=UserControl}}"
						 SelectedValue="{Binding MyProperty, Mode=TwoWay}"
						 Loaded="MyComboBox_Loaded"
						 Tag="{Binding}"
						 SelectionChanged="Entry_SelectionChanged" />
```

```csharp
private void MyComboBox_Loaded( object sender, RoutedEventArgs e )
{
	if ( sender is MairaComboBox combo && combo.ItemsSource == null )
	{
		combo.ItemsSource = _myOptions;

		if ( combo.Tag is MyEntryClass entry )
		{
			combo.SelectedValue = entry.MyProperty;
		}
	}
}
```

- Check `combo.ItemsSource == null` to prevent re-initialization on virtualizing panel recycling.
- Restore `SelectedValue` from `Tag` after setting `ItemsSource` (setting `ItemsSource` clears the selection).

### Localized ComboBox Items

Build options in an `UpdateComboBoxOptions()` method called by `App` on language change:

```csharp
private List<KeyValuePair<MyEnum, string>> _myOptions = [];

public void UpdateComboBoxOptions()
{
	var loc = DataContext.DataContext.Instance.Localization;
	_myOptions = new Dictionary<MyEnum, string>
	{
		{ MyEnum.ValueA, loc["MyEnumValueA"] },
		{ MyEnum.ValueB, loc["MyEnumValueB"] },
	}.ToList();

	RefreshLists();
}
```

---

## MairaButton

A circular icon button with two size variants.

| Property | Type | Default | Notes |
|---|---|---|---|
| `Label` | `string` | `""` | Optional text label |
| `LabelOnRight` | `bool` | `false` | Places label to the right of the ring |
| `Icon` | `ImageSource` | — | Icon inside the ring |
| `IsSmall` | `bool` | `false` | Use small ring assets |
| `Disabled` | `bool` | `false` | Disables the button |
| `Click` | event | — | `RoutedEventHandler`; sender is the `MairaButton` |

**Sizing rules:**
- Omit `IsSmall` for standalone action buttons (e.g., "Add").
- Set `IsSmall="True"` for inline buttons alongside text inputs (e.g., Browse, Remove).

**Icon resource URI format:**
```xml
Icon="/MarvinsAIRARefactored;component/Artwork/Buttons/my-icon.png"
```

**DataTemplate pattern:**
```xml
<controls:MairaButton Icon="/MarvinsAIRARefactored;component/Artwork/Buttons/browse.png"
					   IsSmall="True"
					   Tag="{Binding}"
					   Click="BrowseButton_Click"
					   VerticalAlignment="Bottom" />
```
```csharp
private void BrowseButton_Click( object sender, RoutedEventArgs e )
{
	if ( sender is MairaButton button && button.Tag is MyEntryClass entry )
	{
		// use entry
	}
}
```

---

## Artwork / Icon PNGs

All button icons are 96 × 96 px PNGs:

| Property | Value |
|---|---|
| Canvas | 96 × 96 px, transparent background |
| Stroke | White, 4.0–4.5 px pen |
| Content footprint | ~40 × 40 px centered at (48, 48) |
| Smoothing | `AntiAlias`, `Round` caps and joins |
| Format | PNG 32-bit with alpha |

Icons are generated via **GDI+ PowerShell scripts** using `System.Drawing`.

After creating a PNG, register it in `MarvinsAIRARefactored.csproj` in **alphabetical order** within the button artwork `<ItemGroup>`:
```xml
<Resource Include="Artwork\Buttons\my-new-icon.png" />
```
Failing to register causes a runtime `IOException` on XAML load.

---

## List Entries with Colored Left Bar

Standard layout for editable list entries:

```xml
<Grid Margin="0,0,0,20">
  <Grid.ColumnDefinitions>
	<ColumnDefinition Width="4" />
	<ColumnDefinition Width="12" />
	<ColumnDefinition Width="*" />
  </Grid.ColumnDefinitions>

  <Border Grid.Column="0" Background="#e04040" CornerRadius="2" Opacity="0.75" />

  <Grid Grid.Column="2">
	<!-- controls go here -->
  </Grid>
</Grid>
```

| Color meaning | Hex |
|---|---|
| Terminate / danger | `#e04040` (red) |
| Start / positive | `#44b060` (green) |

Always use `CornerRadius="2"` and `Opacity="0.75"`.

---

## Dialog Windows — Template

```xml
<Window x:Class="MarvinsAIRARefactored.Windows.MyDialog"
		xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
		xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		Title="My Dialog"
		Width="600" Height="480"
		ResizeMode="NoResize"
		WindowStyle="SingleBorderWindow"
		WindowStartupLocation="CenterOwner"
		Icon="/Artwork/AppIcon/maira-universal.ico">
```

Opening from a `UserControl`:
```csharp
var dialog = new MyDialog { Owner = Window.GetWindow( this ) };
dialog.ShowDialog();
```

---

## Async Window Loading Pattern

When a dialog must enumerate data on open:
1. Show a "Searching…" overlay in the same grid row as the results list.
2. In the constructor, hook `Loaded` with an `async` lambda.
3. Use `Task.Run` to enumerate on a background thread.
4. After `await`, collapse the overlay and populate the list.

```csharp
public MyDialog()
{
	InitializeComponent();

	Loaded += async ( _, _ ) =>
	{
		SearchBox.Focus();
		await LoadDataAsync();
	};
}

private async Task LoadDataAsync()
{
	var items = await Task.Run( () => GetItems() );

	_allItems = items;
	SearchingText.Visibility = Visibility.Collapsed;
	ApplyFilter();
}
```

```xml
<TextBlock x:Name="SearchingText"
		   Text="Searching..."
		   HorizontalAlignment="Center"
		   VerticalAlignment="Center"
		   IsHitTestVisible="False" />
```

---

## Search / Filter Text Box with Watermark

```xml
<Grid>
  <TextBox x:Name="SearchBox" TextChanged="SearchBox_TextChanged" />
  <TextBlock IsHitTestVisible="False" FontStyle="Italic" Foreground="#80ffffff">
	<TextBlock.Style>
	  <Style TargetType="TextBlock">
		<Setter Property="Visibility" Value="Collapsed" />
		<Style.Triggers>
		  <DataTrigger Binding="{Binding Text, ElementName=SearchBox}" Value="">
			<Setter Property="Visibility" Value="Visible" />
		  </DataTrigger>
		</Style.Triggers>
	  </Style>
	</TextBlock.Style>
	Filter by name...
  </TextBlock>
</Grid>
```

---

## KeyEventArgs Disambiguation

The project references both `System.Windows.Forms` and `System.Windows.Input`. Add this alias to every `.xaml.cs` file that handles keyboard events:

```csharp
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
```

---

## XAML Files — Write Without BOM

When writing `.xaml` files programmatically, always use UTF-8 **without** BOM. Using `Set-Content -Encoding UTF8` in PowerShell writes a BOM that breaks the XAML code-generator and causes `CS0103` errors.

```powershell
[System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
```

---

## Excluding the Current App from Process Lists

```csharp
.Where( p => !string.Equals( p.Path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase ) )
```
