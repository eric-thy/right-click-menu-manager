using System;
using System.Windows;
using System.Windows.Input;

namespace Cmm
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute)
            : this(execute, null)
        {
        }

        public RelayCommand(Action<object> execute, Predicate<object> canExecute)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            var handler = CanExecuteChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }

    public class DialogRequest
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string PrimaryText { get; set; }
        public string SecondaryText { get; set; }
        public bool Danger { get; set; }
        public Action PrimaryAction { get; set; }
        public Action SecondaryAction { get; set; }
        public UIElement Extra { get; set; }
        public string Glyph { get; set; }
    }

    public class ToastRequest
    {
        public string Message { get; set; }
        public bool CanUndo { get; set; }
        public bool IsError { get; set; }
    }
}
