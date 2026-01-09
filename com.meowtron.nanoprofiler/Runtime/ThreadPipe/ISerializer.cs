namespace NanoProfiler
{
    /// <summary>
    /// Visitor interface for message pipe data serialization
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// Transfers any unmanaged data type to/from a data stream
        /// </summary>
        public void Transfer<T>(ref T value) where T : unmanaged;
        
        /// <summary>
        /// Transfers managed string to/from a data stream 
        /// </summary>
        public void Transfer(ref string value);

        /// <summary>
        /// Align data stream. Should be used after types that might
        /// leave stream unaligned, such as types smaller than 32bit
        /// or strings.
        /// </summary>
        public void Align();

        /// <summary>
        /// Advances serializer to the next message in a data stream
        /// </summary>
        /// <returns>>true if the data stream can advance to the next message or false otherwise</returns>
        public bool Advance();
        
        /// <summary>
        /// Value in range from 0 to 1 which indicates how full data stream buffer is
        /// </summary>
        public float Contention { get; }
    }
}