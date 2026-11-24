
namespace MarvinsAIRARefactored.GameBridges.Ams2;

// data source seam for the Automobilista 2 bridge - live shared memory today, a capture player later
public abstract class Ams2DataProvider
{
	public abstract bool TryOpen();
	public abstract void Close();

	// copies the whole block into destination; returns false when the page is not open or the copy was torn
	// by a concurrent write (the caller simply keeps the previous frame and tries again next sub-sample)
	public abstract bool TryReadBlock( byte[] destination );
}
