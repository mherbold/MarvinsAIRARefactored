using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MarvinsAIRARefactored.Classes;

public class AppManagerStartEntryViewModel : INotifyPropertyChanged
{
	public AppManagerStartEntry Entry { get; }

	private bool _isFirst;
	public bool IsFirst
	{
		get => _isFirst;
		set
		{
			if ( _isFirst != value )
			{
				_isFirst = value;
				OnPropertyChanged();
				OnPropertyChanged( nameof( IsNotFirst ) );
			}
		}
	}

	private bool _isLast;
	public bool IsLast
	{
		get => _isLast;
		set
		{
			if ( _isLast != value )
			{
				_isLast = value;
				OnPropertyChanged();
				OnPropertyChanged( nameof( IsNotLast ) );
			}
		}
	}

	public bool IsNotFirst => !_isFirst;
	public bool IsNotLast => !_isLast;

	public AppManagerStartEntryViewModel( AppManagerStartEntry entry )
	{
		Entry = entry;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
	{
		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}
}
