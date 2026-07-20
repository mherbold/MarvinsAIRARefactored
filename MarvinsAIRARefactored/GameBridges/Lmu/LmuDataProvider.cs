
namespace MarvinsAIRARefactored.GameBridges.Lmu;

public enum LmuBufferType
{
	Telemetry = 0,
	Scoring = 1,
	ForceFeedback = 2,
	Extended = 3,
	Rules = 4,
	NativeData = 5
}

public abstract class LmuDataProvider : IDisposable
{
	public abstract bool TryOpen();

	public abstract void Close();

	public abstract bool TryReadBuffer( LmuBufferType bufferType, byte[] destination );

	public void Dispose()
	{
		Close();

		GC.SuppressFinalize( this );
	}
}
