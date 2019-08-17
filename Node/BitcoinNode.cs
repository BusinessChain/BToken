using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BToken.Accounting;
using BToken.Chaining;
using BToken.Networking;

namespace BToken
{
  public partial class BitcoinNode
  {
    public Network Network { get; private set; }
    Headerchain Headerchain;
    UTXO UTXO;
    Wallet Wallet;

    BitcoinGenesisBlock GenesisBlock = new BitcoinGenesisBlock();
    List<HeaderLocation> Checkpoints = new List<HeaderLocation>()
      {
        new HeaderLocation(height : 11111, hash : "0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d"),
        new HeaderLocation(height : 250000, hash : "000000000000003887df1f29024b06fc2200b55f8af8f35453d7be294df2d214"),
        new HeaderLocation(height : 535419, hash : "000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39")
      };

    public BitcoinNode()
    {
      Network = new Network();
      Headerchain = new Headerchain(GenesisBlock.Header, Checkpoints);
      UTXO = new UTXO(GenesisBlock, Headerchain, Network);
      Wallet = new Wallet(UTXO);
    }

    public async Task StartAsync()
    {
      Network.Start();

      await Headerchain.LoadFromArchiveAsync();
      Console.WriteLine("Loaded headerchain from archive, height '{0}'", Headerchain.GetHeight());

      await Network.RunSessionAsync(new SessionHeaderDownload(Headerchain));
      Console.WriteLine("downloaded headerchain from network, height '{0}'", Headerchain.GetHeight());

      await UTXO.StartAsync();

      //Task NetworkListenerTask = StartNetworkListenerAsync();

      //Wallet.GeneratePublicKey();
    }

    async Task StartNetworkListenerAsync()
    {
      while (true)
      {
        INetworkChannel channel = await Network.AcceptChannelInboundRequestAsync();
        try
        {
          List<NetworkMessage> inboundMessages = channel.GetInboundRequestMessages();

          foreach (NetworkMessage inboundMessage in inboundMessages)
          {
            switch (inboundMessage.Command)
            {
              case "inv":
                //await ProcessInventoryMessageAsync(invMessage);
                break;

              case "getheaders":
                //var getHeadersMessage = new GetHeadersMessage(inboundMessage);
                //var headers = Headerchain.GetHeaders(getHeadersMessage.HeaderLocator, getHeadersMessage.StopHash);
                //await channel.SendMessageAsync(new HeadersMessage(headers));
                break;

              case "headers":
                var headersMessage = new HeadersMessage(inboundMessage);
                List<byte[]> headersInserted = await Headerchain.InsertHeadersAsync(headersMessage.Headers);
                //await UTXO.NotifyBlockHeadersAsync(headersInserted, channel);
                break;

              case "block":
                break;

              default:
                break;
            }
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine("Serving inbound request of channel '{0}' ended in exception '{1}'",
            channel.GetIdentification(),
            ex.Message);

          //Network.RemoveChannel(channel);
        }
      }
    }
  }
}
