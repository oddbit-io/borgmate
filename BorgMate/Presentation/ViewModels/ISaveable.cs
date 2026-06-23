using System.ComponentModel;

namespace BorgMate.Presentation.ViewModels;

public interface ISaveable : INotifyPropertyChanged
{
    bool IsSaved { get; }
}