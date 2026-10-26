using System;
using System.Windows.Input;

namespace IfcComparison.Utils
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> mExecute;
        private readonly Predicate<object> mCanExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            mExecute = execute ?? throw new ArgumentNullException(nameof(execute));
            mCanExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return mCanExecute == null ? true : mCanExecute(parameter);
        }

        public void Execute(object parameter)
        {
            mExecute.Invoke(parameter);
        }
    }
}
