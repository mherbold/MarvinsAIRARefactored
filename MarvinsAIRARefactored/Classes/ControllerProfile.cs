
namespace MarvinsAIRARefactored.Classes;

// A named, complete set of button mappings. Users who swap wheels (rims) on the same wheelbase
// switch between profiles manually - the wheelbase device GUID never changes, only the button
// numbers the rim reports, so there is no way to auto-detect the active rim.
//
// The dictionary is keyed by the Settings property name (e.g. "RacingWheelStrengthPlusButtonMappings").
// The flat Settings.*ButtonMappings properties remain the live working copy of the currently selected
// profile; Settings.SaveCurrentControllerProfile / ApplyControllerProfile snapshot mappings in and out
// of this store.
public class ControllerProfile
{
	public const string DefaultProfileName = "Default";

	public string Name { get; set; } = DefaultProfileName;

	// The default profile is named using the localized word for "Default" in the user's current language
	// at the moment it is first created. The stored name is only a label and dictionary key - nothing keys
	// off the literal "Default" after creation - so it is safe to persist the name in any language.
	public static string GetLocalizedDefaultProfileName()
	{
		var localizedName = DataContext.DataContext.Instance.Localization[ "Default" ];

		return ( string.IsNullOrEmpty( localizedName ) || localizedName.StartsWith( '!' ) ) ? DefaultProfileName : localizedName;
	}

	public SerializableDictionary<string, ButtonMappings> ButtonMappings { get; set; } = [];
}
