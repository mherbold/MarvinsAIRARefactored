using System.Drawing;

namespace MarvinsAIRARefactored.DataContext;

// Holds the position and scale for every overlay window that persists a layout.
// One instance is the "non-car" layout (used when per-car overlays are disabled, and as the
// template for cars seen for the first time); additional instances are stored per car in
// Settings.OverlaysCarLayoutDictionary. All fields are value types, so MemberwiseClone is a deep copy.
public class OverlayLayoutSettings
{
	public Rectangle GapMonitorWindowPosition { get; set; } = Rectangle.Empty;
	public float GapMonitorWindowScale { get; set; } = 1f;

	public Rectangle DeltaMonitorWindowPosition { get; set; } = Rectangle.Empty;
	public float DeltaMonitorWindowScale { get; set; } = 1f;

	public Rectangle GripOMeterWindowPosition { get; set; } = Rectangle.Empty;
	public float GripOMeterWindowScale { get; set; } = 1f;

	public Rectangle SpeechToTextWindowPosition { get; set; } = Rectangle.Empty;
	public float SpeechToTextWindowScale { get; set; } = 1f;

	public OverlayLayoutSettings Clone() => (OverlayLayoutSettings) MemberwiseClone();
}
