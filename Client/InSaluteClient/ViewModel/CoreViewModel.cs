using DataAccessLayer;
using InSalute.Models;
using InSalute.Stores;
using InSalute.Utilities;
using MVVMEssentials.Services;
using MVVMEssentials.ViewModels;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.ObjectModel;
using System.Windows.Data;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Globalization;
using System.Windows.Input;
using MVVMEssentials.Commands;
using System.Net.Mail;
using System.Net;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Xml.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;
using ICSharpCode.SharpZipLib.Zip;

namespace InSalute.ViewModel
{
    class CoreViewModel : ViewModelBase
    {
        #region UI buttons
        public DelegateCommand LogoutCommand { get; set; }
        public ICommand NavigateHomeCommand { get; }
        public ICommand NavigateLoginCommand { get; }
        public ICommand NavigateUserCommand { get; }
        public ICommand NavigateManageUserCommand { get; }
        public DelegateCommand LoadConfigurationCommand { get; }
        public DelegateCommand SaveConfigurationCommand { get; }
        public DelegateCommand LoadAttachmentsCommand { get; }
        public DelegateCommand LoadBillingCommand { get; }
        public DelegateCommand SendEmailCommand { get; }

        #endregion UI buttons

        #region UI utilities

        private bool _isManageAllAccountVisible;
        public bool IsManageAllAccountVisible
        {
            get => _isManageAllAccountVisible;
            set
            {
                _isManageAllAccountVisible = value;
                OnPropertyChanged(nameof(IsManageAllAccountVisible));
            }
        }

        private bool _isNotAdmin;
        public bool IsNotAdmin
        {
            get => _isNotAdmin;
            set
            {
                _isNotAdmin = value;
                OnPropertyChanged(nameof(IsNotAdmin));
            }
        }

        private string _displayedUsername = "";
        public string DisplayedUsername
        {
            get => _displayedUsername;
            set
            {
                _displayedUsername = value;
                OnPropertyChanged(nameof(DisplayedUsername));
            }
        }

        #region Sender
        private string _senderEmail = "";

        public string SenderEmail
        {
            get => _senderEmail;
            set
            {
                _senderEmail = value;
                OnPropertyChanged(nameof(SenderEmail));
            }
        }

        private string _senderPassword = "";

        public string SenderPassword
        {
            get => _senderPassword;
            set
            {
                _senderPassword = value;
                OnPropertyChanged(nameof(SenderPassword));
            }
        }

        private bool _senderPasswordVisible = false;

        public bool SenderPasswordVisible
        {
            get => _senderPasswordVisible;
            set
            {
                _senderPasswordVisible = value;
                OnPropertyChanged(nameof(SenderPasswordVisible));
            }
        }
        #endregion Sender

        #region Receiver
        private string _receiverEmail = "";

        public string ReceiverEmail
        {
            get => _receiverEmail;
            set
            {
                _receiverEmail = value;
                OnPropertyChanged(nameof(ReceiverEmail));
            }
        }

        private string _receiverSurname = "";

        public string ReceiverSurname
        {
            get => _receiverSurname;
            set
            {
                _receiverSurname = value;
                OnPropertyChanged(nameof(ReceiverSurname));
            }
        }
        private string _receiverName = "";

        public string ReceiverName
        {
            get => _receiverName;
            set
            {
                _receiverName = value;
                OnPropertyChanged(nameof(ReceiverName));
            }
        }

        private List<string> _receiverAttachments = new List<string>();

        public List<string> ReceiverAttachments
        {
            get => _receiverAttachments;
            set
            {
                _receiverAttachments = value;
                OnPropertyChanged(nameof(ReceiverAttachments));
            }
        }

        private string _receiverBilling = "";

        public string ReceiverBilling
        {
            get => _receiverBilling;
            set
            {
                _receiverBilling = value;
                OnPropertyChanged(nameof(ReceiverBilling));
            }
        }
        #endregion Receiver

        #region Email
        private string _firstObject = "";

        public string FirstObject
        {
            get => _firstObject;
            set
            {
                _firstObject = value;
                OnPropertyChanged(nameof(FirstObject));
            }
        }

        private string _firstEmail = "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph Foreground=\"Black\"></Paragraph></FlowDocument>";

        public string FirstEmail
        {
            get => _firstEmail;
            set
            {
                _firstEmail = value;
                OnPropertyChanged(nameof(FirstEmail));
            }
        }

        private string _secondObject = "";

        public string SecondObject
        {
            get => _secondObject;
            set
            {
                _secondObject = value;
                OnPropertyChanged(nameof(SecondObject));
            }
        }

        private string _secondEmail = "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph Foreground=\"Black\"></Paragraph></FlowDocument>";

        public string SecondEmail
        {
            get => _secondEmail;
            set
            {
                _secondEmail = value;
                OnPropertyChanged(nameof(SecondEmail));
            }
        }


        #endregion Email

        private bool _emailSentCorrectly = true;

        public bool EmailSentCorrectly
        {
            get => _emailSentCorrectly;
            set
            {
                _emailSentCorrectly = value;
                OnPropertyChanged(nameof(EmailSentCorrectly));
            }
        }

        private string _sendEmailText = "Invia";

        public string SendEmailText
        {
            get => _sendEmailText;
            set
            {
                _sendEmailText = value;
                OnPropertyChanged(nameof(SendEmailText));
            }
        }

        private string _encryptPassword = "";
        private string _secureAttachmentPath = "";

        public string EncryptPassword
        {
            get => _encryptPassword;
            set
            {
                _encryptPassword = value;
                OnPropertyChanged(nameof(EncryptPassword));
            }
        }

        #endregion UI utilities

        private readonly UserStore UserStore;

        public CoreViewModel(UserStore userStore, INavigationService homeNavigationService,
            INavigationService loginNavigationService, INavigationService userNavigationService, INavigationService manageUserNavigationService)
        {
            LogoutCommand = new DelegateCommand(Logout);

            NavigateHomeCommand = new NavigateCommand(homeNavigationService);
            NavigateLoginCommand = new NavigateCommand(loginNavigationService);
            NavigateUserCommand = new NavigateCommand(userNavigationService);
            NavigateManageUserCommand = new NavigateCommand(manageUserNavigationService);
            LoadConfigurationCommand = new DelegateCommand(LoadConfiguration);
            SaveConfigurationCommand = new DelegateCommand(SaveConfiguration);
            LoadAttachmentsCommand = new DelegateCommand(LoadAttachments);
            LoadBillingCommand = new DelegateCommand(LoadBilling);
            SendEmailCommand = new DelegateCommand(SendEmail);

            NavigateLoginCommand.Execute(null);

            UserStore = userStore;
            ChangeUIForUser(UserStore.CurrentUser);
        }

        private void SaveConfiguration()
        {
            CommonSaveFileDialog saveFileDialog = new CommonSaveFileDialog
            {
                EnsurePathExists = true,
                EnsureFileExists = false
            };
            saveFileDialog.Filters.Add(new CommonFileDialogFilter("File json", "*.json"));

            if (saveFileDialog.ShowDialog() == CommonFileDialogResult.Ok && !string.IsNullOrWhiteSpace(saveFileDialog.FileName))
            {
                var configs = new
                {
                    mail = SenderEmail,
                    first_object = FirstObject,
                    second_object = SecondObject,
                    first_text = GetContent(FirstEmail),
                    second_text = GetContent(SecondEmail)
                };

                string jsonData = JsonConvert.SerializeObject(configs);
                File.WriteAllText(saveFileDialog.FileName, jsonData);
            }
        }

        private async void LoadConfiguration()
        {
            CommonOpenFileDialog openFileDialog = new CommonOpenFileDialog
            {
                Multiselect = false,
                IsFolderPicker = false
            };

            openFileDialog.Filters.Add(new CommonFileDialogFilter("File json", "*.json"));
            if (openFileDialog.ShowDialog() == CommonFileDialogResult.Ok && !string.IsNullOrWhiteSpace(openFileDialog.FileName))
            {
                using (StreamReader sr = File.OpenText(openFileDialog.FileName))
                {
                    string configurationFileContent = await sr.ReadToEndAsync();
                    JToken token = JsonConvert.DeserializeObject<JToken>(configurationFileContent)["mail"];
                    if (token != null)
                    {
                        SenderEmail = token.ToObject<string>();
                    }

                    token = JsonConvert.DeserializeObject<JToken>(configurationFileContent)["first_object"];
                    if (token != null)
                    {
                        FirstObject = token.ToObject<string>();
                    }

                    token = JsonConvert.DeserializeObject<JToken>(configurationFileContent)["second_object"];
                    if (token != null)
                    {
                        SecondObject = token.ToObject<string>();
                    }

                    token = JsonConvert.DeserializeObject<JToken>(configurationFileContent)["first_text"];
                    if (token != null)
                    {
                        FirstEmail = $"<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph Foreground=\"Black\">{token.ToObject<string>()}</Paragraph></FlowDocument>";
                    }

                    token = JsonConvert.DeserializeObject<JToken>(configurationFileContent)["second_text"];
                    if (token != null)
                    {
                        SecondEmail = $"<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph Foreground=\"Black\">{token.ToObject<string>()}</Paragraph></FlowDocument>";
                    }
                }
            }
        }

        private void Logout()
        {
            UserStore.CurrentUser = null;
            NavigateHomeCommand.Execute(null);
            NavigateLoginCommand.Execute(null);
        }

        private void ChangeUIForUser(UserExtended user)
        {
            if (user != null)
            {
                IsManageAllAccountVisible = user.Role.ToLower() == "admin" || user.Role.ToLower() == "manager";
                IsNotAdmin = user.Role != "admin";
                DisplayedUsername = user.Username + " (" + user.Role + ")";
            }
            else
            {
                IsManageAllAccountVisible = false;
                IsNotAdmin = true;
                DisplayedUsername = "";
            }
        }

        private void SendEmail()
        {
            if (CheckFields())
            {
                if (EmailSentCorrectly)
                {
                    if (SendEmail(SenderEmail, SenderPassword, ReceiverEmail, FirstObject, FirstEmail, ReceiverBilling, ReceiverAttachments))
                    {
                        if (SendEmail(SenderEmail, SenderPassword, ReceiverEmail, SecondObject, SecondEmail))
                        {
                            EmailSentCorrectly = true;
                            SendEmailText = "Invia";
                        }
                        else
                        {
                            MessageBox.Show("C'è stato un errore durante l'invio della seconda email, riprova ad inviarla.", "Errore durante l'invio", MessageBoxButton.OK, MessageBoxImage.Error);
                            EmailSentCorrectly = false;
                            SendEmailText = "Riprova";
                        }
                    }
                    else
                    {
                        MessageBox.Show("C'è stato un errore durante l'invio della prima email, riprova ad inviarla.", "Errore durante l'invio", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    if (SendEmail(SenderEmail, SenderPassword, ReceiverEmail, SecondObject, SecondEmail))
                    {
                        EmailSentCorrectly = true;
                        SendEmailText = "Invia";
                    }
                    else
                    {
                        MessageBox.Show("C'è stato un errore durante l'invio della seconda email, riprova ad inviarla.", "Errore durante l'invio", MessageBoxButton.OK, MessageBoxImage.Error);
                        EmailSentCorrectly = false;
                        SendEmailText = "Riprova";
                    }
                }

                if (EmailSentCorrectly)
                {
                    ReceiverEmail = "";
                    ReceiverName = "";
                    ReceiverSurname = "";
                    ReceiverBilling = "";
                    ReceiverAttachments = new List<string>();
                    EncryptPassword = "";
                    _secureAttachmentPath = "";
                    MessageBox.Show("Email inviate correttamente", "Email inviate", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private bool SendEmail(string sender, string senderPwd, string receiver, string subject, string body, string billing = "", List<string> attachments = null)
        {
            bool success = true;
            using MailMessage mail = new MailMessage(sender, receiver);
            mail.Subject = subject;
            mail.Body = GetContent(body);
            if (!string.IsNullOrWhiteSpace(billing))
            {
                mail.Attachments.Add(new Attachment(billing));
            }

            if (attachments != null && attachments.Count > 0)
            {
                if (_secureAttachmentPath == "")
                {
                    if (ReceiverAttachments.Count == 1 && ReceiverAttachments[0].EndsWith(".pdf"))
                    {
                        _secureAttachmentPath = SecureFile(ReceiverAttachments[0]);
                    }
                    else if (ReceiverAttachments.Count > 1)
                    {
                        _secureAttachmentPath = SecureFile(ReceiverAttachments);
                    }
                }
                mail.Attachments.Add(new Attachment(_secureAttachmentPath));
            }

            mail.IsBodyHtml = false;
            SmtpClient smtp = new SmtpClient
            {// TODO FIX
                //Host = "smtp.gmail.com",
                Host = "smtp-mail.outlook.com",
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                //Credentials = new NetworkCredential(from, "otucenwipsktxxdd"),
                //Credentials = new NetworkCredential(SenderEmail, "trupihrvchpcswdf"),
                Credentials = new NetworkCredential(sender, senderPwd),
                EnableSsl = true,
                Port = 25
            };
            try
            {
                smtp.Send(mail);
                // TODO ADD LOG TO DB
            }
            catch (SmtpException e)
            {
                success = false;
                Console.WriteLine(e.Message);
            }

            return success;
        }

        private bool CheckFields()
        {
            bool errors = false;
            string errorMessage = "Ho riscontrato un problema controllando i campi:" + Environment.NewLine;
            if (!IsValidEmail(SenderEmail))
            {
                errors = true;
                errorMessage += "- Email mittente non valida." + Environment.NewLine;
            }

            if (string.IsNullOrWhiteSpace(SenderPassword))
            {
                errors = true;
                errorMessage += "- Password mittente vuota." + Environment.NewLine;
            }

            if (!IsValidEmail(ReceiverEmail))
            {
                errors = true;
                errorMessage += "- Email destinatario non valida." + Environment.NewLine;
            }

            if (string.IsNullOrWhiteSpace(FirstObject))
            {
                errors = true;
                errorMessage += "- Oggetto della prima email vuoto." + Environment.NewLine;
            }

            if (string.IsNullOrWhiteSpace(GetContent(FirstEmail)))
            {
                errors = true;
                errorMessage += "- Contenuto della prima email vuoto." + Environment.NewLine;
            }

            if (string.IsNullOrWhiteSpace(SecondObject))
            {
                errors = true;
                errorMessage += "- Oggetto della seconda email vuoto." + Environment.NewLine;
            }

            if (string.IsNullOrWhiteSpace(GetContent(SecondEmail)))
            {
                errors = true;
                errorMessage += "- Contenuto della seconda email vuoto." + Environment.NewLine;
            }

            if (errors)
            {
                MessageBox.Show(errorMessage, "Errore di inserimento", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                MailAddress addr = new MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private void LoadAttachments()
        {
            CommonOpenFileDialog openFileDialog = new CommonOpenFileDialog
            {
                Multiselect = true,
                IsFolderPicker = false
            };

            if (openFileDialog.ShowDialog() == CommonFileDialogResult.Ok && openFileDialog.FileNames.Count() > 0)
            {
                ReceiverAttachments.Clear();
                foreach (string attachment in openFileDialog.FileNames)
                {
                    ReceiverAttachments.Add(attachment);
                }
                OnPropertyChanged(nameof(ReceiverAttachments));
            }
        }

        private void LoadBilling()
        {
            CommonOpenFileDialog openFileDialog = new CommonOpenFileDialog
            {
                Multiselect = false,
                IsFolderPicker = false
            };

            openFileDialog.Filters.Add(new CommonFileDialogFilter("File pdf", "*.pdf"));
            if (openFileDialog.ShowDialog() == CommonFileDialogResult.Ok && !string.IsNullOrWhiteSpace(openFileDialog.FileName))
            {
                ReceiverBilling = openFileDialog.FileName;
            }
        }

        private string GetContent(string richText)
        {
            XDocument doc = XDocument.Parse(richText);
            XElement paragraph = doc.Root.Descendants().First();
            string rawRichText = paragraph.Value;
            rawRichText = rawRichText.Replace("{NOME}", ReceiverName);
            rawRichText = rawRichText.Replace("{COGNOME}", ReceiverSurname);
            string convertedText = rawRichText.Replace("{PASSWORD}", EncryptPassword);
            return convertedText;
        }

        private string SecureFile(string filename)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            PdfDocument document = PdfReader.Open(filename);

            PdfSecuritySettings securitySettings = document.SecuritySettings;
            EncryptPassword = Guid.NewGuid().ToString();
            securitySettings.UserPassword = EncryptPassword;

            // Restrict some rights.
            securitySettings.PermitAccessibilityExtractContent = false;
            securitySettings.PermitAnnotations = false;
            securitySettings.PermitAssembleDocument = false;
            securitySettings.PermitExtractContent = false;
            securitySettings.PermitFormsFill = true;
            securitySettings.PermitFullQualityPrint = false;
            securitySettings.PermitModifyDocument = true;
            securitySettings.PermitPrint = false;

            string secured_file = filename.Substring(0, filename.IndexOf(".pdf")) + "_encrypted.pdf";
            document.Save(secured_file);
            return secured_file;
        }

        private string SecureFile(List<string> files)
        {
            string filename = Path.Combine(Path.GetDirectoryName(files[0]),
                !string.IsNullOrWhiteSpace(ReceiverName) && !string.IsNullOrWhiteSpace(ReceiverSurname) ?
                ReceiverSurname + ReceiverName + ".zip" : "attachments.zip");
            using (ZipFile zipFile = ZipFile.Create(filename))
            {
                zipFile.BeginUpdate();

                files.ForEach(x =>
                {
                    if (Path.HasExtension(x))
                    {
                        zipFile.Add(x);
                    }

                    else if (!Path.HasExtension(x) && Directory.Exists(x))
                    {
                        Directory.GetFiles(x, "*.*", SearchOption.AllDirectories).ToList().ForEach(zipFile.Add);
                    }
                });
                EncryptPassword = Guid.NewGuid().ToString();
                zipFile.Password = EncryptPassword;

                zipFile.UseZip64 = UseZip64.On;

                zipFile.CommitUpdate();
                zipFile.Close();

            }

            return filename;
        }
    }
}
