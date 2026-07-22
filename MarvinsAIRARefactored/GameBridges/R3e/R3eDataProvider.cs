
namespace MarvinsAIRARefactored.GameBridges.R3e;

public enum R3eBufferType
{
	Shared = 0,
	Probe = 1
}

public abstract class R3eDataProvider : IDisposable
{
	public abstract bool TryOpen();

	public abstract void Close();

	public abstract bool TryReadBuffer( R3eBufferType bufferType, byte[] destination );

	public void Dispose()
	{
		Close();

		GC.SuppressFinalize( this );
	}
}
