using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SecureExamPlatform.Models
{
    /// <summary>
    /// ViewModel for question navigation in the UI
    /// </summary>
    public class QuestionViewModel : INotifyPropertyChanged
    {
        private string _id;
        private int _number;
        private bool _isAnswered;
        private bool _isCurrent;

        public string Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged();
            }
        }

        public int Number
        {
            get => _number;
            set
            {
                _number = value;
                OnPropertyChanged();
            }
        }

        public bool IsAnswered
        {
            get => _isAnswered;
            set
            {
                _isAnswered = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ButtonStyle));
            }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                _isCurrent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ButtonStyle));
            }
        }

        /// <summary>
        /// Returns the appropriate style name for the question button
        /// </summary>
        public string ButtonStyle
        {
            get
            {
                if (IsCurrent) return "CurrentQuestion";
                if (IsAnswered) return "AnsweredQuestion";
                return "UnansweredQuestion";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}