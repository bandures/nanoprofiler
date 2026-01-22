using System;
using NUnit.Framework;

namespace NanoProfiler.Tests
{
    public static class MessagePipeTests
    {
        [Test, Timeout(1000)]
        public static void MessagePipe_NonPow2Size_ThrowsException()
        {
            Assert.Throws(typeof(ArgumentException), () =>
            {
                var messagePipe = new MessagePipe(123);
            });
        }

        [Test, Timeout(1000)]
        public static void MessagePipe_ReadAfterWrite_DataIsTheSame()
        {
            const int nMessagePipeSize = 4096;
            const uint magicValue = 0xDEADBEEF;
            using var messagePipe = new MessagePipe(nMessagePipeSize);

            var magicNumber = magicValue;
            var sender = messagePipe.Writer;
            sender.Transfer(ref magicNumber);
            sender.Advance();

            var receiver = messagePipe.Reader;
            Assert.IsTrue(receiver.Advance());

            receiver.Transfer(ref magicNumber);
            Assert.AreEqual(magicValue, magicNumber);
        }

        [Test, Timeout(1000)]
        public static void MessagePipe_WriteOverflows_DiscardsData()
        {
            const int messagePipeSize = 32;
            const uint magicValue = 0xDEADBEEF;
            using var messagePipe = new MessagePipe(messagePipeSize);

            var magicNumber = magicValue;
            var sender = messagePipe.Writer;
            for (var i = 0; i < 10; i++)
                sender.Transfer(ref magicNumber);
            Assert.IsFalse(sender.Advance());
        }

        struct TestMessage15Byte : Protocol.IMessage
        {
            public int B1; // 4
            public int B2; // 8
            public int B3; // 12
            public ushort B4; // 14
            public byte B5; // 15
            
            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref B1);
                serializer.Transfer(ref B2);
                serializer.Transfer(ref B3);
                serializer.Transfer(ref B4);
                serializer.Transfer(ref B5);
            }
        }
        
        [Test, Timeout(1000)]
        // Test when writer catches up to reader at exact the same position.
        // We need 15byte message + 1 byte size
        // ----
        // I should treat adding "size" as any other transfers, so that it does a proper size check
        // this test fails because "size" silently advances position beyond read pos and after that
        // we just overwrite what is being read
        // ---
        public static void MessagePipe_WriteOverflowsAtExactZeroSize_DiscardsData()
        {
            const int messagePipeSize = 1024;
            using var messagePipe = new MessagePipe(messagePipeSize);

            var sender = messagePipe.Writer;
            var message = new TestMessage15Byte();
            
            // Fully fill the buffer in
            // 64 x 16 = 1024
            for (var i = 0; i < 63; i++)
            {
                message.Transfer(sender);
                Assert.IsTrue(sender.Advance());
            }
            
            // This message should fail
            message.Transfer(sender);
            Assert.IsFalse(sender.Advance());
        }

        [Test, Timeout(1000)]
        public static void MessagePipe_LargerMsg_DataIsTheSame()
        {
            const int messageLength = 254;
            const int messagePipeSize = 1024;
            using var messagePipe = new MessagePipe(messagePipeSize);

            var sender = messagePipe.Writer;
            for (byte i = 0; i < messageLength; i++)
                sender.Transfer(ref i);
            Assert.IsTrue(sender.Advance());
            
            var receiver = messagePipe.Reader;
            Assert.IsTrue(receiver.Advance());
            for (byte i = 0; i < messageLength; i++)
            {
                byte val = 0;
                receiver.Transfer(ref val);
                Assert.AreEqual(i, val);
            }
        }

        [Test, Timeout(1000)]
        public static void MessagePipe_MsgMoreThan256byte_Fails()
        {
            const int messagePipeSize = 1024;
            const uint magicValue = 0xDEADBEEF;
            using var messagePipe = new MessagePipe(messagePipeSize);

            var magicNumber = magicValue;
            var sender = messagePipe.Writer;
            for (var i = 0; i < 64; i++)
                sender.Transfer(ref magicNumber);
            Assert.IsFalse(sender.Advance());
        }

        [Test, Timeout(1000)]
        public static void MessagePipe_WithSentMessages_SentBytesCounted()
        {
            const int messagePipeSize = 1024;
            const uint magicValue = 0xDEADBEEF;
            using var messagePipe = new MessagePipe(messagePipeSize);

            var magicNumber = magicValue;
            var sender = messagePipe.Writer;
            for (var i = 0; i < 16; i++)
                sender.Transfer(ref magicNumber);
            sender.Advance();

            messagePipe.GetStatistics(out var sentBytes, out var discardedBytes);
            Assert.AreEqual(sizeof(int) * 16, sentBytes);
            Assert.AreEqual(0, discardedBytes);
        }

        [Test, Timeout(1000)]
        public static void MessagePipe_WithOverflow_DiscardedBytesCounted()
        {
            const int messagePipeSize = 64;
            const uint magicValue = 0xDEADBEEF;
            using var messagePipe = new MessagePipe(messagePipeSize);

            var magicNumber = magicValue;
            var sender = messagePipe.Writer;
            for (int i1 = 0; i1 < 4; i1++)
            {
                for (var i2 = 0; i2 < 12; i2++)
                    sender.Transfer(ref magicNumber);
                sender.Advance();
            }

            messagePipe.GetStatistics(out var sentBytes, out var discardedBytes);
            Assert.AreEqual(sizeof(int) * 12, sentBytes);
            Assert.AreEqual(sizeof(int) * 36, discardedBytes);
        }

        [Test, Timeout(1000)]
        public static void MessagePipe_WithOversizeMessage_DiscardedBytesCounted()
        {
            const int messagePipeSize = 1024;
            const uint magicValue = 0xDEADBEEF;
            using var messagePipe = new MessagePipe(messagePipeSize);

            var magicNumber = magicValue;
            var sender = messagePipe.Writer;
            for (var i = 0; i < 64; i++)
                sender.Transfer(ref magicNumber);
            sender.Advance();

            messagePipe.GetStatistics(out var sentBytes, out var discardedBytes);
            Assert.AreEqual(0, sentBytes);
            Assert.AreEqual(sizeof(int) * 64, discardedBytes);
        }
    }
}