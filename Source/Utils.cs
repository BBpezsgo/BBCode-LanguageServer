namespace LanguageServer
{
    using LanguageCore;

    [Serializable]
    public class ServiceException : Exception
    {
        public ServiceException(string message) : base(message) { }
        public ServiceException(string message, Exception inner) : base(message, inner) { }
        protected ServiceException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    public static class Extensions
    {
        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> self, TKey key, TValue value) where TKey : notnull
        {
            if (self.TryGetValue(key, out TValue? _value))
            {
                return _value;
            }
            self.Add(key, value);
            return value;
        }

        public static string Extension(this DocumentUri uri)
            => System.IO.Path.GetExtension(uri.ToUri().AbsolutePath).TrimStart('.').ToLowerInvariant();

        public static string Extension(this TextDocumentIdentifier uri)
            => System.IO.Path.GetExtension(uri.Uri.ToUri().AbsolutePath).TrimStart('.').ToLowerInvariant();

        public static Range ToOmniSharp(this Range<SinglePosition> self) => new()
        {
            Start = self.Start.ToOmniSharp(),
            End = self.End.ToOmniSharp(),
        };

        public static OmniSharp.Extensions.LanguageServer.Protocol.Models.Position ToOmniSharp(this SinglePosition self) => new()
        {
            Line = Math.Max(self.Line, 0),
            Character = Math.Max(self.Character, 0),
        };

        public static Range ToOmniSharp(this Position self) => new()
        {
            Start = self.Range.Start.ToOmniSharp(),
            End = self.Range.End.ToOmniSharp(),
        };

        public static Range<SinglePosition> ToCool(this Range self) => new()
        {
            Start = self.Start.ToCool(),
            End = self.End.ToCool(),
        };

        public static SinglePosition ToCool(this OmniSharp.Extensions.LanguageServer.Protocol.Models.Position self) => new()
        {
            Line = Math.Max(self.Line, 0),
            Character = Math.Max(self.Character, 0),
        };
    }
}
