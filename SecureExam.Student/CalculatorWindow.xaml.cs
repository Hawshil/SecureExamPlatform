using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SecureExam.Student
{
    public partial class CalculatorWindow : Window
    {
        private string currentInput = "0";
        private string storedValue = "";
        private string currentOperation = "";
        private bool shouldResetDisplay = false;

        public CalculatorWindow()
        {
            InitializeComponent();
            UpdateDisplay();

            // Allow dragging the window
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        }

        private void Number_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            string number = button.Content.ToString();

            if (shouldResetDisplay || currentInput == "0")
            {
                currentInput = (number == ".") ? "0." : number;
                shouldResetDisplay = false;
            }
            else
            {
                if (number == "." && currentInput.Contains("."))
                    return;
                currentInput += number;
            }

            UpdateDisplay();
        }

        private void Operation_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            string operation = button.Content.ToString();

            if (!string.IsNullOrEmpty(currentOperation) && !shouldResetDisplay)
            {
                Calculate();
            }

            storedValue = currentInput;
            currentOperation = operation;
            shouldResetDisplay = true;
        }

        private void Equals_Click(object sender, RoutedEventArgs e)
        {
            Calculate();
            currentOperation = "";
            shouldResetDisplay = true;
        }

        private void Calculate()
        {
            if (string.IsNullOrEmpty(storedValue) || string.IsNullOrEmpty(currentOperation))
                return;

            try
            {
                double num1 = double.Parse(storedValue);
                double num2 = double.Parse(currentInput);
                double result = 0;

                switch (currentOperation)
                {
                    case "+":
                        result = num1 + num2;
                        break;
                    case "-":
                        result = num1 - num2;
                        break;
                    case "×":
                        result = num1 * num2;
                        break;
                    case "÷":
                        if (num2 != 0)
                            result = num1 / num2;
                        else
                        {
                            currentInput = "Error";
                            UpdateDisplay();
                            return;
                        }
                        break;
                    case "%":
                        result = num1 % num2;
                        break;
                }

                currentInput = result.ToString();
                UpdateDisplay();
            }
            catch
            {
                currentInput = "Error";
                UpdateDisplay();
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            currentInput = "0";
            storedValue = "";
            currentOperation = "";
            shouldResetDisplay = false;
            UpdateDisplay();
        }

        private void Backspace_Click(object sender, RoutedEventArgs e)
        {
            if (currentInput.Length > 1)
            {
                currentInput = currentInput.Substring(0, currentInput.Length - 1);
            }
            else
            {
                currentInput = "0";
            }
            UpdateDisplay();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void UpdateDisplay()
        {
            DisplayText.Text = currentInput;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Prevent actual closing, just hide
            e.Cancel = true;
            this.Hide();
        }
    }
}
