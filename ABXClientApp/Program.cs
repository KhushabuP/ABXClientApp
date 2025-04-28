using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;

public class ABXClient
{
    private readonly string _hostname;
    private readonly int _port = 3000;

    public ABXClient(string hostname)
    {
        _hostname = hostname;
    }

    public void Start()
    {
        List<int> receivedSequences = new List<int>();
        List<OrderPacket> allPackets = new List<OrderPacket>();

        // Step 1: Connect to server
        using (TcpClient client = new TcpClient())
        {
            client.Connect(_hostname, _port);
            using (NetworkStream stream = client.GetStream())
            {
                // Step 2: Send callType 1 (Stream All Packets)
                byte[] request = new byte[2];
                request[0] = 1; // callType 1
                request[1] = 0; // resendSeq (not used for callType 1)

                stream.Write(request, 0, request.Length);

                // Step 3: Read responses until server closes connection
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buffer = new byte[1024];
                    int bytesRead;

                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                    }

                    byte[] allData = ms.ToArray();
                    int packetSize = 17; // 4 + 1 + 4 + 4 + 4

                    for (int i = 0; i < allData.Length; i += packetSize)
                    {
                        byte[] packetBytes = new byte[packetSize];
                        Array.Copy(allData, i, packetBytes, 0, packetSize);
                        OrderPacket packet = ParsePacket(packetBytes);
                        allPackets.Add(packet);
                        receivedSequences.Add(packet.PacketSequence);
                    }
                }
            }
        }

        // Step 4: Detect missing sequences
        int minSeq = receivedSequences.Min();
        int maxSeq = receivedSequences.Max();
        HashSet<int> receivedSet = new HashSet<int>(receivedSequences);
        List<int> missingSequences = new List<int>();

        for (int seq = minSeq; seq <= maxSeq; seq++)
        {
            if (!receivedSet.Contains(seq))
            {
                missingSequences.Add(seq);
            }
        }

        Console.WriteLine($"Received {allPackets.Count} packets.");
        Console.WriteLine($"Missing {missingSequences.Count} packets.");

        // Step 5: Request missing packets
        foreach (int missingSeq in missingSequences)
        {
            Console.WriteLine($"Requesting missing packet {missingSeq}...");
            OrderPacket resentPacket = RequestResend(missingSeq);
            if (resentPacket != null)
            {
                allPackets.Add(resentPacket);
            }
        }

        Console.WriteLine("Completed receiving all packets!");
    }

    private OrderPacket RequestResend(int sequence)
    {
        using (TcpClient client = new TcpClient())
        {
            client.Connect(_hostname, _port);
            using (NetworkStream stream = client.GetStream())
            {
                byte[] request = new byte[2];
                request[0] = 2; // callType 2
                request[1] = (byte)sequence; // resendSeq

                stream.Write(request, 0, request.Length);

                // Only one packet is expected
                byte[] packetBytes = new byte[17];
                int bytesRead = stream.Read(packetBytes, 0, packetBytes.Length);
                if (bytesRead == 17)
                {
                    return ParsePacket(packetBytes);
                }
            }
        }
        return null;
    }

    private OrderPacket ParsePacket(byte[] data)
    {
        if (data.Length != 17)
            throw new ArgumentException("Invalid packet size");

        string symbol = Encoding.ASCII.GetString(data, 0, 4);
        char buySellIndicator = (char)data[4];

        int quantity = ReadInt32BigEndian(data, 5);
        int price = ReadInt32BigEndian(data, 9);
        int packetSequence = ReadInt32BigEndian(data, 13);

        return new OrderPacket
        {
            Symbol = symbol,
            BuySellIndicator = buySellIndicator,
            Quantity = quantity,
            Price = price,
            PacketSequence = packetSequence
        };
    }

    private int ReadInt32BigEndian(byte[] buffer, int startIndex)
    {
        return (buffer[startIndex] << 24) |
               (buffer[startIndex + 1] << 16) |
               (buffer[startIndex + 2] << 8) |
               (buffer[startIndex + 3]);
    }
}

public class OrderPacket
{
    public string Symbol { get; set; }
    public char BuySellIndicator { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public int PacketSequence { get; set; }
}
namespace ABXClientApp
{
    class Program
    {
        static void Main(string[] args)
        {
            ABXClient client = new ABXClient("127.0.0.1"); // Use your server IP here
            client.Start();
        }
    }
}