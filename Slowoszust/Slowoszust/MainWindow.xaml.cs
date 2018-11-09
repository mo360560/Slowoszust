using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;

namespace Slowoszust
{

    public enum WORD_CHECK_RESULT : byte { NOT_A_PREFIX, PREFIX, WORD };
    
    public partial class MainWindow : Window
    {
        public static readonly byte BOARD_X = 4;
        public static readonly byte BOARD_Y = 4;
        private ListBox word_list;
        private bool starting_page = true;
        private WordFinder word_finder;
        private TextBox[,] letter_box = new TextBox[BOARD_X, BOARD_Y];
        public char LetterAt(int i, int j) {
            if (letter_box[i, j].Text == "") return '?';
            return letter_box[i, j].Text[0];
        }

        public MainWindow()
        {
            InitializeComponent();
            Topmost = true;
            GenerateBoard();
            EventManager.RegisterClassHandler(typeof(TextBox), TextBox.TextChangedEvent, new TextChangedEventHandler(TextBox_TextChanged));
            word_finder = new WordFinder(this);
        }

        //Handling letter input
        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            var isText = e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true);
            if (!isText) return;

            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            HandlePaste(text, sender);

            e.Handled = true;
        }

        private void HandlePaste(string text, object sender)
        {
            var letters = text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            byte i = (byte)((TextBox)sender).TabIndex;
            byte j = 0;
            foreach (string letter in letters)
            {
                letter_box[i / BOARD_X, i % BOARD_Y].Text = letters[j][0].ToString();
                letter_box[i / BOARD_X, i % BOARD_Y].Focus();
                ++i;
                ++j;
                if (i >= BOARD_X*BOARD_Y) break;
            }
        }

        void TextBox_TextChanged(object sender, EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            TraversalRequest tRequest = new TraversalRequest(FocusNavigationDirection.Next);
            UIElement keyboardFocus = Keyboard.FocusedElement as UIElement;
            if (keyboardFocus != null) keyboardFocus.MoveFocus(tRequest);
        }

        //UI
        private void GenerateBoard()
        {
            //Creating 4x4 grid for letters
            Style style = this.FindResource("LetterBoxStyle") as Style;
            int Left = 0, Top = 0;
            for (byte i = 0; i < BOARD_X; ++i)
            {
                for (byte j = 0; j < BOARD_Y; ++j)
                {
                    letter_box[i, j] = new TextBox();
                    letter_box[i, j].Name = "Letter" + (i * BOARD_X + j).ToString();
                    letter_box[i, j].Text = "";
                    letter_box[i, j].Style = style;
                    letter_box[i, j].TabIndex = i * BOARD_X + j;
                    Thickness margin = letter_box[i, j].Margin;
                    margin.Left = Left;
                    margin.Top = Top;
                    letter_box[i, j].Margin = margin;
                    MainGrid.Children.Add(letter_box[i, j]);
                    DataObject.AddPastingHandler(letter_box[i, j], OnPaste);
                    Left += 55;
                }
                Left = 0;
                Top += 55;
            }
            letter_box[0, 0].Focus();
        }

        private void GenerateList()
        {
            //Creating list to show all found words
            Style style = this.FindResource("ListBoxStyle") as Style;
            word_list = new ListBox();
            word_list.Style = style;
            MainGrid.Children.Add(word_list);
        }

        private void BeginPlay(object sender, RoutedEventArgs e)
        {
            if (word_finder.loading_dictionary) return;
            if (starting_page)
            {
                if (!word_finder.CheckBoard()) return; //not a legal board
                ChangePageToWordList();
                word_finder.LookForWords();
                word_list.Items.SortDescriptions.Add(
                    new System.ComponentModel.SortDescription("Length",
                    System.ComponentModel.ListSortDirection.Descending)
                ); //Longer words count for more points, it's convenient to have them on top of the list
            }
            else
            {
                ChangePageToStartPage();
            }
        }

        private void ChangePageToWordList()
        {
            starting_page = false;
            MainGrid.Children.RemoveRange(0, BOARD_X * BOARD_Y);
            Description.Content = "Lista słów:";
            PlayButton.Content = "Wróć";
            GenerateList();
        }

        private void ChangePageToStartPage()
        {
            starting_page = true;
            MainGrid.Children.Remove(word_list);
            Description.Content = "Ustaw planszę i graj!";
            PlayButton.Content = "Graj!";
            GenerateBoard();
        }

        private void TopmostOn(object sender, RoutedEventArgs e)
        {
            ToggleTopmost.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#FFFFFF");
            Topmost = true;
        }

        private void TopmostOff(object sender, RoutedEventArgs e)
        {
            ToggleTopmost.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#555555");
            Topmost = false;
        }

        public void AddWordToList(string word)
        {
            if (!word_list.Items.Contains(word)) word_list.Items.Add(word);
        }

        public void ChangeButtonText(string text)
        {
            this.Dispatcher.Invoke(() => {
                PlayButton.Content = text;
            });
        }
    }

    public partial class WordFinder
    {

        public bool loading_dictionary { get; private set; } = true;
        private MainWindow slowotok_window;
        private Trie game_dictionary = new Trie();
        private char[,] board = new char[MainWindow.BOARD_X, MainWindow.BOARD_Y];
        private bool[,] used = new bool[MainWindow.BOARD_X, MainWindow.BOARD_Y];

        public WordFinder(MainWindow sw)
        {
            slowotok_window = sw;
            ThreadPool.QueueUserWorkItem(GenerateDictionary, "pack://application:,,,/PolishDictionary.txt");
        }

        private void GenerateDictionary(object path)
        {
            //Creates TRIE from PolishDictionary.
            //PolishDictionary is prepared by me specifically for Słowotok. 
            slowotok_window.ChangeButtonText("Ładowanie słownika...");
            var uri = new Uri(path.ToString());
            var resourceStream = Application.GetResourceStream(uri);
            using (StreamReader sr = new StreamReader(resourceStream.Stream))
            {
                while (sr.Peek() >= 0)
                {
                    string word = sr.ReadLine();
                    game_dictionary.AddWord(word);
                }
            }
            loading_dictionary = false;
            slowotok_window.ChangeButtonText("Graj!");
        }

        public bool CheckBoard()
        {
            //See if the whole board is set and if there aren't any illegal characters
            for (byte i = 0; i < MainWindow.BOARD_X; ++i)
            {
                for (byte j = 0; j < MainWindow.BOARD_Y; ++j)
                {
                    used[i, j] = false;
                    if (!Node.ALPHABET.Contains(slowotok_window.LetterAt(i, j))) return false;
                    board[i, j] = slowotok_window.LetterAt(i, j);
                    if (!Char.IsLetter(board[i, j])) return false;
                }
            }
            return true;
        }

        public void LookForWords()
        {
            for (byte i = 0; i < MainWindow.BOARD_X; ++i)
                for (byte j = 0; j < MainWindow.BOARD_Y; ++j)
                    TryWord(i, j, "");
        }

        private void TryWord(byte x, byte y, String current_word)
        {
            //Trying all possible letter combinations and checking if the created string exists in a dictionary
            if (x < 0 || x > 3 || y < 0 || y > 3 || used[x, y]) return;
            used[x, y] = true;
            current_word += board[x, y];
            WORD_CHECK_RESULT result = game_dictionary.CheckWord(current_word);
            switch (result)
            {
                case WORD_CHECK_RESULT.NOT_A_PREFIX:
                    break;
                case WORD_CHECK_RESULT.WORD:
                    slowotok_window.AddWordToList(current_word);
                    goto case WORD_CHECK_RESULT.PREFIX;
                case WORD_CHECK_RESULT.PREFIX:
                    for (sbyte i = -1; i <= 1; ++i)
                        for (sbyte j = -1; j <= 1; ++j)
                            TryWord((byte)(x + i), (byte)(y + j), current_word);
                    break;
            }
            used[x, y] = false;
        }

    }

    //Trie based on toreaurstad.blogspot.com/2014/07/implementing-fast-dictionary-for.html
    public class Node
    {

        private const byte ALPH_LENGTH = 32; //without q, v and x
        public static readonly char[] ALPHABET = new char[32]
        { 'A', 'Ą', 'B', 'C', 'Ć', 'D', 'E', 'Ę', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'Ł',
          'M', 'N', 'Ń', 'O', 'Ó', 'P', 'R', 'S', 'Ś', 'T', 'U', 'W', 'Y', 'Z', 'Ź', 'Ż', };
  
        private readonly Node[] children = new Node[ALPH_LENGTH];

        public IEnumerable<KeyValuePair<Node, char>> AssignedChildren
        {
            get
            {
                for (byte i = 0; i < ALPH_LENGTH; i++)
                {
                    if (children[i] != null)
                        yield return new KeyValuePair<Node, char>(children[i], ALPHABET[i]);
                }
            }
        }

        public Node GetOrCreate(char c)
        {
            Node child = this[c];
            if (child == null)
                child = this[c] = new Node();
            return child;
        }

        public Node this[char c]
        {
            get { return children[Array.IndexOf(ALPHABET, c)]; }
            set { children[Array.IndexOf(ALPHABET, c)] = value; }
        }

        public bool IsWordTerminator { get; set; }

    }

    public class Trie
    {

        private readonly Node root = new Node();

        private Node NodeForWord(string word, bool createPath)
        {
            Node current = root;
            foreach (char c in word)
            {
                if (createPath) current = current.GetOrCreate(c);
                else current = current[c];
                if (current == null) return null;
            }
            return current;
        }

        public void AddWord(string word)
        {
            Node node = NodeForWord(word.ToUpper(), true);
            node.IsWordTerminator = true;
        }

        public WORD_CHECK_RESULT CheckWord(string word)
        {
            Node node = NodeForWord(word, false);
            if (node == null) return WORD_CHECK_RESULT.NOT_A_PREFIX;
            else if (node.IsWordTerminator) return WORD_CHECK_RESULT.WORD;
            else return WORD_CHECK_RESULT.PREFIX;
        }

    }

}