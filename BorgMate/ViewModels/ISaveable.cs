using System.ComponentModel;

namespace BorgMate.ViewModels;

public interface ISaveable : INotifyPropertyChanged
{
    bool IsSaved { get; }
}
