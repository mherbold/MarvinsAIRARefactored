
namespace MarvinsAIRARefactored.GameBridges.Ac;

public abstract class AcDataProvider : IDisposable
{
	public abstract bool TryOpen();

	public abstract void Close();

	public abstract bool TryReadBuffer( AcBufferType bufferType, byte[] destination );

	public void Dispose()
	{
		Close();

		GC.SuppressFinalize( this );
	}
}
