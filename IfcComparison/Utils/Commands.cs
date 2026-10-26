using System;
using System.Windows.Input;

namespace IfcComparison.Utils
{
    public class CommandHandler : ICommand
    {
        private Action mAction;
        private bool mCanExecute;

        public CommandHandler(Action action, bool canExecute)
        {
            mAction = action;
            mCanExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            if (parameter != null)
            {
                mCanExecute = (bool)parameter;
            }
            return mCanExecute;
        }

        public void Execute(object parameter)
        {
            mAction();
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
