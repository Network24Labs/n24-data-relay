namespace N24DataRelay.Core.Interfaces;

/// <summary>Creates IFileTransferService instances based on the current configuration.</summary>
public interface IFileTransferServiceFactory
{
    IFileTransferService CreateTransferService();
}
