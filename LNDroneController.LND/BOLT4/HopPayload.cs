using Kermalis.EndianBinaryIO;
using LNDroneController.Extentions;
using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LNDroneController.LND
{
    public class HopPayload
    {
        public HopPayloadType HopPayloadType { get; set; }
        public byte[] ChannelId { get; set; } = null;
        public ulong AmountToForward { get; set; }
        public uint OutgoingCltvValue { get; set; }
        public byte[] PaymentMetadata { get; set; }

        public List<TLV> OtherTLVs { get; set; } = new List<TLV>();
        public int Size
        {
            get
            {
                if (HopPayloadType == HopPayloadType.Legacy)
                {
                    return 32;
                }
                var dataBuffer = this.ToDataBuffer();
                return dataBuffer.Length;
            }
        }
        public int SphinxSize
        {
            get
            {
                if (HopPayloadType == HopPayloadType.Legacy)
                {
                    return 33;
                }
                var payloadLength = Size;
                var varint = new BigSize((ulong)payloadLength);
                return varint.Length + payloadLength;
            }
        }

        public PaymentData PaymentData { get; private set; }

        public byte[] ToSphinxBuffer()
        {
            var dataBuffer = ToDataBuffer();
            if (this.HopPayloadType == HopPayloadType.TLV)
            {
                var varint = new BigSize((ulong)this.Size);
                return varint.Encoding.Concat(dataBuffer).ToArray();
            }
            return (new byte[1]).Concat(dataBuffer).ToArray();
        }

        public byte[] ToDataBuffer()
        {
            if (HopPayloadType != HopPayloadType.Legacy)
            {  
                var amountToForwardBufferX = AmountToForward.UInt64ToTrimmedBE64Bytes(); //  EndianBitConverter.UInt64sToBytes(AmountToForward.InArray(), 0, 1, Endianness.BigEndian).TrimZeros();
                var amountToForwardTlv = new TLV(HopPayloadTLVTypes.AMOUNT_TO_FORWARD, amountToForwardBufferX);

                var outgoingCltvValueBuffer = OutgoingCltvValue.UInt32ToTrimmedBE32Bytes(); // EndianBitConverter.UInt32sToBytes(OutgoingCltvValue.InArray(), 0, 1, Endianness.BigEndian).TrimZeros();
                var outgoingCltvValueTlv = new TLV(HopPayloadTLVTypes.OUTGOING_CLTV_VALUE, outgoingCltvValueBuffer);

                var channelIdTlvBuffer = new List<byte>();
                if (ChannelId != null && ChannelId.Length > 0)
                {
                    var channelIdTlv = new TLV(HopPayloadTLVTypes.SHORT_CHANNEL_ID, ChannelId);
                    channelIdTlvBuffer.AddRange(channelIdTlv.ToEncoding());
                }
                return amountToForwardTlv.ToEncoding().Concat(outgoingCltvValueTlv.ToEncoding()).Concat(channelIdTlvBuffer).ToArray();
            }
            var buffer = new byte[32];
            if (ChannelId != null && ChannelId.Length > 0)
            {
                ChannelId.CopyTo(buffer, 0);
            }
            var amountToForwardBuffer = AmountToForward.UInt64ToBE64(); 
            amountToForwardBuffer.CopyTo(buffer, 8);
            OutgoingCltvValue.UInt32ToBE32().CopyTo(buffer, 16);
            return buffer;
        }

        public static HopPayload ParseSphinx(byte[] undelimitedHopPayloads)
        {
            Span<byte> hopBytes = new Span<byte>(undelimitedHopPayloads);
            var firstByte = undelimitedHopPayloads[0];
            if (firstByte == 0) //legacy
            {
                var sphinxBuffer = hopBytes.Slice(0, 33);
                var dataBuffer = sphinxBuffer.Slice(1);
                var channelId = dataBuffer.Slice(0, 8);
                var amountToForward = EndianBitConverter.BytesToUInt64s(dataBuffer.Slice(8, 16).ToArray(), 0, 1, Endianness.BigEndian).First();
                var outgoingCltvValue = EndianBitConverter.BytesToUInt32s(dataBuffer.Slice(16, 4).ToArray(), 0, 1, Endianness.BigEndian).First();
                return new HopPayload
                {
                    HopPayloadType = HopPayloadType.Legacy,
                    ChannelId = channelId.ToArray(),
                    AmountToForward = amountToForward,
                    OutgoingCltvValue = outgoingCltvValue,
                };
            }
            else
            {
                var bigSize = BigSize.Parse(undelimitedHopPayloads);
                int dataLength = (int)bigSize.Value;
                int lengthEncodedLength = (int)bigSize.Length;
                var remainingStream = undelimitedHopPayloads[lengthEncodedLength..(lengthEncodedLength + dataLength)];
                var tlvs = new List<TLV>();
                while (remainingStream.Length > 0)
                {
                    var currentTlv = TLV.Parse(remainingStream);
                    remainingStream = remainingStream[currentTlv.TLVSize..];
                    tlvs.Add(currentTlv);
                }
                var hopPayload = new HopPayload
                {
                    HopPayloadType = HopPayloadType.TLV,
                };

                foreach (var tlv in tlvs)
                {
                    var currentType = tlv.Type;
                    if (currentType == HopPayloadTLVTypes.AMOUNT_TO_FORWARD)
                    {
                        hopPayload.AmountToForward = tlv.Value.TrimmedBE64ToUInt64();  // EndianBitConverter.BytesToUInt64s(tlv.Value, 0, 1, Endianness.BigEndian).First();
                    }
                    else if (currentType == HopPayloadTLVTypes.OUTGOING_CLTV_VALUE)
                    {
                        hopPayload.OutgoingCltvValue = tlv.Value.TrimmedBE32ToUInt32(); //EndianBitConverter.BytesToUInt32s(tlv.Value, 0, 1, Endianness.BigEndian).First();
                    }
                    else if (currentType == HopPayloadTLVTypes.SHORT_CHANNEL_ID)
                    {
                        hopPayload.ChannelId = tlv.Value;
                    }
                    else if (currentType == HopPayloadTLVTypes.PAYMENT_DATA)
                    {
                        hopPayload.PaymentData = new PaymentData(tlv.Value);
                    }
                    else if (currentType == HopPayloadTLVTypes.PAYMENT_METADATA)
                    {
                        hopPayload.PaymentMetadata = tlv.Value;
                    }
                    else
                    {
                        hopPayload.OtherTLVs.Add(tlv);
                    }
                }

                return hopPayload;
            }
        }
    }

    public class PaymentData
    {
        public byte[] RawData { get; private set; }

        public byte[] PaymentSecret { get; private set; }
        public ulong TotalMSat { get; private set; }

        public PaymentData(byte[] rawData)
        {
            RawData = rawData;
            PaymentSecret = rawData[0..32];
            TotalMSat = rawData[32..].TrimmedBE64ToUInt64();
        }
    }
}
