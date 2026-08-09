using System.Collections.Generic;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Core.Interfaces {
    public interface IVariableRepository {
        IList<VariableItem> LoadAll ();
        void SaveAll (IList<VariableItem> items);
    }
}