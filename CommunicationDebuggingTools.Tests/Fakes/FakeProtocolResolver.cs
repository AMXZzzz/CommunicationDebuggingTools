using System.Collections.Generic;
using CommunicationDebuggingTools.Core.Interfaces;

namespace CommunicationDebuggingTools.Tests.Fakes {
    public class FakeProtocolResolver : IProtocolResolver {
        public IProtocol ProtocolToReturn { get; set; }

        public void LoadFromFolder (string folder) { }

        public IProtocol Resolve (string protocolName) {
            if (ProtocolToReturn != null &&
                ProtocolToReturn.GetProtocolName() == protocolName)
                return ProtocolToReturn;
            return null;
        }

        public IList<string> GetProtocolNames () {
            if (ProtocolToReturn == null)
                return new List<string>();
            return new List<string> { ProtocolToReturn.GetProtocolName() };
        }
    }
}