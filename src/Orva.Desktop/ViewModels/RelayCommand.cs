using System;
using System.Windows.Input;

namespace Orva.ViewModels
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;
        private readonly Action<object> _executeWithParameter;
        private readonly Predicate<object> _canExecuteWithParameter;

        public RelayCommand(Action execute)
            : this(execute, null)
        {
        }

        public RelayCommand(Action execute, Func<bool> canExecute)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            _execute = execute;
            _canExecute = canExecute;
        }

        public RelayCommand(Action<object> execute, Predicate<object> canExecute)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            _executeWithParameter = execute;
            _canExecuteWithParameter = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            if (_executeWithParameter != null)
                return _canExecuteWithParameter == null || _canExecuteWithParameter(parameter);

            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;

            if (_executeWithParameter != null)
                _executeWithParameter(parameter);
            else
                _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            var handler = CanExecuteChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}