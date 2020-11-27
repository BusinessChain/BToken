using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class NetworkAddress
  {
    public UInt32 UnixTimeSeconds { get; private set; }
    public UInt64 NetworkServices { get; private set; }
    public IPAddress IPAddress { get; private set; }
    public UInt16 Port { get; private set; }


    public NetworkAddress(
      UInt32 unixTimeSeconds,
      UInt64 networkServices,
      IPAddress iPAddress,
      UInt16 port)
    {
      UnixTimeSeconds = unixTimeSeconds;
      NetworkServices = networkServices;
      IPAddress = iPAddress;
      Port = port;
    }

    public static NetworkAddress ParseAddress(byte[] buffer, ref int startIndex)
    {
      UInt32 unixTimeSeconds = BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      UInt64 networkServices = BitConverter.ToUInt64(buffer, startIndex);
      startIndex += 8;

      IPAddress iPAddress = new IPAddress(buffer.Skip(startIndex).Take(16).ToArray());
      startIndex += 16;

      UInt16 port = BitConverter.ToUInt16(buffer, startIndex);
      startIndex += 2;

      return new NetworkAddress(
        unixTimeSeconds,
        networkServices,
        iPAddress,
        port);
    }
  }
}
