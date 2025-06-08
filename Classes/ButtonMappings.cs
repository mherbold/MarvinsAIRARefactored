
namespace MarvinsAIRARefactored.Classes;

public class ButtonMappings
{
	public class MappedButton
	{
		public class Button
		{
			public string DeviceProductName { get; set; } = string.Empty;
			public Guid DeviceInstanceGuid { get; set; } = Guid.Empty;
			public int ButtonNumber { get; set; } = 0;
		}

		public Button HoldButton { get; set; } = new();
		public Button ClickButton { get; set; } = new();
	}

	public List<MappedButton> MappedButtons { get; } = [];
}
