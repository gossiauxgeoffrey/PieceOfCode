using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using iTask.Gateway_iTask;
using iTask.Mapping;
using iTask.Models.Identity;
using iTask.Modules;
using iTask.ViewModels;
using iTask.ViewModels.Conversation;
using iTaskGatewayService.ServiceiTask;
using System.Web;
using iTask.ViewModels.Utils;
using iTask.Controllers;

namespace iTask.Helpers
{
    public static class ConversationHelper 
    {
        public static async Task<ConversationViewModel> FillConversationFromTask(long taskId, ServiceGateWayClient gatewayITask, EnvironmentViewModel environnement, TaskUser user, List<ClAgent> fullAgents, ClTaskDocument currentTask = null)
        {
            var model = new ConversationViewModel();
            var task = new ClTaskDocument();

            
            var userPreference = await PreferenceHelper.GetPreferenceSettings(gatewayITask, user);

            //pour pouvoir avoir les quelques infos de la tâche ( description, employeur et worker de la tâche)
            if (currentTask != null)
                task = currentTask;
            else
            {
                task = await Task.Run(() => gatewayITask.SearchDocumentTask(taskId, environnement.language, true,
                    MapClRessourcesToRessourcesViewModel.Map(user.RessourcesListWithAccess).ToArray(), long.Parse(user.Id)));
            }

            var contentConversation = gatewayITask.GetTaskConversation(task).OrderBy(a => a.SentDate);
            

            if (taskId == -1)
            {
                //todo something
            }
            else
            {
                model.TaskId = taskId;
                model.CurrentStatus = task.CurrentStatus;
                model.Description = task.Task.Description;
                model.TaskEmployerNumber = task.Task.EmployerNumber;
                model.TaskEmployerName = task.Task.EmployerDenomination;
                model.TaskWorkerNumber = task.Task.WorkerNumber;
                model.TaskWorkerName = task.Task.WorkerName;
                model.DialogDisplayChrono = userPreference.DialogDisplayChrono;
                model.IsStatusVisible = true;

                // 05/09/2019 Optimisation
               // List<string> courtesyFormulas = gatewayITask.GetCourtesyFormula().ToList();

                //if (courtesyFormulas == null)
                //    courtesyFormulas = new List<string>();


                //on prend celles qui correspondent à l'id
                foreach (var elem in contentConversation)
                {
                    
                    if (CanSeeMessage(elem, user, gatewayITask))
                    {
                        var senderMail = CreateALiasFormated(elem.AuthorAgent.Mail, elem.AliasFrom);//GetFullNameMail(fullAgents, elem.AuthorAgent.Mail);
                        var mess = new MessageConversationViewModel
                        {
                            TypeOfMessage = elem.ContributionType.ToString(),
                            DateOfMessage = elem.SentDate,
                            Sender = senderMail,
                            Subject = elem.Subject,
                            //MessageContentText = MailHelper.FormatBody(elem.MailBody, true, courtesyFormulas),
                            MessageContentText = elem.MailBodyText,
                            MessageContentHtml = elem.MailBody,
                            IsConfidential = elem.Confidential == 1,
                            FilesAssociatedToMessage = new List<DocumentViewModel>(),
                            FullHtmlToRecover = "<div style=\"color:black;\">" + elem.MailBody + "</div>",
                            PermIdMainMessage = elem.PermIdMainMessage,
                            TechnicalIdContributeTask = elem.IdTechnic,
                            MailBodyIsHtml = elem.MailBodyIsHtml,
                            AliasCopyCc= new List<string>(elem.AliasCopyCc),
                            AliasRecipient=new List<string>(elem.AliasRecipient),
                            AliasFrom=elem.AliasFrom              
                        };

                        // Lc le 10/09/2019, toujours prendre le contenu html du mail
                        if (string.IsNullOrEmpty(mess.MessageContentHtml))
                            mess.MessageContentHtml = mess.MessageContentText;

                        // Lors de l'envoi du mail, le tag GroupSMailExternTask a été rajouté
                        // Donc on peut couper pour l'affichage tout ce qui se trouve dans cette Div
                        try
                        {
                            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                            doc.LoadHtml(mess.MessageContentHtml);
                            var divs = doc.DocumentNode.SelectNodes("//div");
                            if (divs != null)
                            {
                                foreach (var tag in divs)
                                {
                                    //if (tag.Attributes["id"] != null && string.Compare(tag.Attributes["id"].Value, "MailExternTask", StringComparison.InvariantCultureIgnoreCase) == 0)
                                    if (tag.Attributes["id"] != null && tag.Attributes["id"].Value.Contains("MailExternTask") )   
                                    {
                                        tag.Remove();
                                    }
                                }

                                mess.MessageContentHtml = doc.DocumentNode.InnerHtml;
                            }
                        }
                        catch { }
                        //



                        mess.AgentVm = MapClAgentToAgentViewModel.Map(elem.AuthorAgent);




                        //switch (elem.ContributionType)
                        //{
                        //    case 1://TASK_TYPE_CONTRIBUTION 1 Mail provenant de l'extérieur
                        //        if (elem.MailBodyText != "")
                        //        {
                        //            mess.MessageContentText = MailHelper.FormatBody(elem.MailBodyText, false, courtesyFormulas);
                        //        }
                        //        else
                        //        {
                        //            mess.MessageContentText = MailHelper.FormatBody(elem.MailBody, false, courtesyFormulas);
                        //        }
                                
                        //        break;
                        //    case 10:
                        //        //TODO message ayant ete envoyé vers l'exterieur... pour de la clarté on retire la banniere pour la conversation 
                        //        mess.MessageContentText = MailHelper.DisplayNoneBanner(mess.MessageContentText);
                        //        break;
                        //}


                        foreach (var cc in elem.CopyCc)
                        {
                            if (cc != "0")
                            {
                                mess.CopyTo += cc + ", ";
                            }
                        }
                        if(!string.IsNullOrEmpty(mess.CopyTo))
                            mess.CopyTo = mess.CopyTo.Substring(0, mess.CopyTo.Length);
                        
                        foreach (var rc in elem.Recipient)
                        {
                            if (rc != "0")
                            {
                                
                                mess.Receiver += rc + ", ";
                            }
                        }
                        if (!string.IsNullOrEmpty(mess.Receiver))
                            mess.Receiver = mess.Receiver.Substring(0, mess.Receiver.Length);

                        //Permet de savoir si il y a plus d'une seule addresse dans le to
                        var isManyReceiver = mess.Receiver!=null? mess.Receiver.Split(','): new string [1];
                        List<string> listReceiver = new List<string>(isManyReceiver);
                        //Supprime la derniere cellule car elle est egale a " " comme on split sur les ','
                        listReceiver.RemoveAt(listReceiver.Count() - 1);
                        mess.CanSeeReplyAllButton = listReceiver.Count() > 1 || mess.CopyTo!=null ? true : false;

                        //si ce n'est pas un commentaire
                        if (mess.TypeOfMessage != "2")
                        {
                            //var recipients = string.Join(",", elem.Recipient);

                            //if (!string.IsNullOrEmpty(recipients) && recipients != "0")
                            //{
                            //    var adrTo = GetFullNameMailSplitComma(fullAgents, recipients.Substring(0, recipients.Length));

                            //    mess.MoreInfosAboutMessage = "TO : " + adrTo;
                            //}
                            var adrTo=CreateChainOfAddressMailWithAlias(elem.AliasRecipient, elem.Recipient);
                            if (adrTo.Length > 0)
                            {
                                //Retire la derniere virgule inutile
                                adrTo=adrTo.Remove(adrTo.Length - 1);
                                mess.MoreInfosAboutMessage = "TO : " + adrTo;
                            }

                            var adrCC=CreateChainOfAddressMailWithAlias(elem.AliasCopyCc, elem.CopyCc);
                            if (adrCC.Length > 0)
                            {
                                //Retire la derniere virgule inutile
                                adrCC=adrCC.Remove(adrCC.Length - 1);
                                mess.MoreInfosAboutMessage += ",CC : " + adrCC;
                            }


                            //var cc = string.Join(",", elem.CopyCc);
                            //if (!string.IsNullOrEmpty(cc) && cc != "0")
                            //if (elem.AliasCopyCc.Length>0 && elem.CopyCc.Length>0)
                            //{
                            //    var adrCC = "";

                            //    for(int i=0; elem.CopyCc.Length>i;i++)
                            //    {
                            //        adrCC += CreateALiasFormated(elem.CopyCc[i],elem.AliasCopyCc[i]) + ",";
                            //    }

                            //    if(adrCC.Length>0)
                            //    {
                            //        //Retire la derniere virgule inutile
                            //        adrCC.Remove(adrCC.Length - 1);
                            //    }

                                ////on est dans le cas on la copie est une chaine de caractere qui contient plusieurs éléments, on va la reformater
                                //if (cc.Contains(","))
                                //{
                                //    adrCC = GetFullNameMailSplitComma(fullAgents, cc.Substring(0, cc.Length));
                                //}
                                //else
                                //{
                                //    adrCC = GetFullNameMail(fullAgents, cc.Substring(0, cc.Length));
                                //}

                            //    mess.MoreInfosAboutMessage += ", CC : " + adrCC;
                            //}

                            // Il arrive que des mails ont été archivés sans adresses dans To et sans CC
                            if (mess.MoreInfosAboutMessage != null)
                            {
                                mess.MoreInfosAboutMessage = mess.MoreInfosAboutMessage.Replace(";", ";\n");
                            }
                        }

                        foreach (var file in elem.joinFiles)
                        {
                            mess.FilesAssociatedToMessage.Add(new DocumentViewModel
                            {
                                DocumentName = file.FileName,
                                PermId = file.PermId
                            });
                        }

                        switch (elem.ContributionType)
                        {
                            case 1://TASK_TYPE_CONTRIBUTION 1 Mail provenant de l'extérieur
                                mess.BubbleCss = "bubble-left mail-content";
                                break;
                            case 2://TASK_TYPE_CONTRIBUTION 2 Commentaire
                                mess.BubbleCss = "bubble-comment comment-content";
                                break;
                            case 3://TASK_TYPE_CONTRIBUTION 3 Mail interne
                                   //si c'est message interne mais c'est la réponse d'un collègue --> bubble-left-internal
                                mess.BubbleCss = elem.AuthorCreation == user.Id ? "bubble-right-internal dialog-content" : "bubble-left-internal dialog-content";
                                break;
                            case 8://TASK_TYPE_CONTRIBUTION 10 Mail destiné à l'extérieur
                                mess.BubbleCss = elem.AuthorCreation == user.Id ? "bubble-right-draft mail-content" : "bubble-left-draft mail-content";
                                break;
                            case 10://TASK_TYPE_CONTRIBUTION 10 Mail destiné à l'extérieur
                                mess.BubbleCss = elem.AuthorCreation == user.Id ? "bubble-right-external mail-content" : "bubble-left-external mail-content";
                                break;
                        }
                        model.MessageConversationList.Add(mess);
                    }
                }

                //mettre le dernier mail externe en reply si il existe
                var lastExt = model.MessageConversationList.LastOrDefault(x => x.TypeOfMessage == "1");
                if (lastExt != null)
                {
                    lastExt.LastExternReply = true;
                }

                //mettre le dernier mail interne en reply si il existe
                var lastInt = model.MessageConversationList.LastOrDefault(x => x.TypeOfMessage == "3");
                if (lastInt != null)
                {
                    lastInt.LastInternReply = true;
                }
            }

            model.Environment = new EnvironmentViewModel
            {
                _listLabels = environnement._listLabels,
                language = environnement.language
            };

            
            if (userPreference.DialogDisplayChrono)
            {
                model.MessageConversationList = model.MessageConversationList.OrderBy(a => a.DateOfMessage).ToList();
            }
            else
            {
                model.MessageConversationList = model.MessageConversationList.OrderByDescending(a => a.DateOfMessage).ToList();
            }
            
            return model;
        }

        public static async Task<ResultViewModel> SaveAndSendNewMessage(SendMessageViewModel model, ServiceGateWayClient gatewayITask, TaskUser user, EnvironmentViewModel environment, List<JoinFileViewModel> newFiles)
        {
            var newFilesTojoin =  MapClJoinFileToJoinFileViewModel.Map(newFiles);
            var existingFilesToAdd = await Task.Run(() => gatewayITask.GetJoinFilesByPermId(model.ExistingFilesToJoin.ToArray()));

            var files = newFilesTojoin.ToList();
            foreach (var existing in existingFilesToAdd)
            {
                var f = existing;
                f.PermIdFileAlreadyArchived = f.PermId;
                files.Add(f);
            }

            var cltaskdocument = new ClTaskDocument
            {
                Task = new ClTask
                {
                    TechnicalId = model.TaskId,
                    Description = model.TaskDescription,
                    EmployerNumber = model.EmployerNumber,
                    WorkerNumber = model.WorkerNumber
                }
            };

            var result = new ResultViewModel
            {
                NoError = true
            };
            //bool isOk = true;
            var task = await Task.Run(() => gatewayITask.SearchDocumentTask(model.TaskId, user.Language, true, MapClRessourcesToRessourcesViewModel.Map(user.RessourcesListWithAccess).ToArray(), long.Parse(user.Id)));

            task.CurrentStatus = model.Status;
            if (task.CurrentStatus != 4)
            {
                task.Task.EffectiveEndDate = DateTime.MinValue;
            }
            switch (model.TypeOfMessage)
            {
                case 1: //Mail provenant de l'extérieur --> on n'enverra pas de mail de ce type
                    break;
                case 2: // commentaire
                    Log.Write("SaveAndSendNewMessage", "Conversation", model.TaskId.ToString(), 3, "debut SaveComment");
                    result.NoError = await SaveComment(gatewayITask, user, cltaskdocument, files, model);
                    Log.Write("SaveAndSendNewMessage", "Conversation", model.TaskId.ToString(), 3, "fin SaveComment");
                    if (result.NoError)
                        result.NoError = await Task.Run(() => gatewayITask.UpdateTaskDocument(task, user.Id, user.CurrentEntityNumber, MapClRessourcesToRessourcesViewModel.Map(user.RessourcesListWithAccess).ToArray(), long.Parse(user.Id)));
                    break;
                case 3: //Mail interne
                    result.NoError = await SaveInternMessage(gatewayITask, user, cltaskdocument, files, model);
                    if (model.AssignToUser)
                        task.Context = int.Parse(model.InternRecipientId);
                    if (result.NoError)
                        result.NoError = await Task.Run(() => gatewayITask.UpdateTaskDocument(task, user.Id, user.CurrentEntityNumber, MapClRessourcesToRessourcesViewModel.Map(user.RessourcesListWithAccess).ToArray(), long.Parse(user.Id)));
                    break;
                case 8:
                case 10: //Mail destiné à l'extérieur
                    var filesForEmail = GetFilesForEmail(existingFilesToAdd, newFilesTojoin);
                    result = await SaveExternMessage(gatewayITask, user, cltaskdocument, files, filesForEmail, model);
                    if (result.NoError && !model.IsDirectEmail)
                    {
                        result.NoError = await Task.Run(() => gatewayITask.UpdateTaskDocument(task, user.Id, user.CurrentEntityNumber,MapClRessourcesToRessourcesViewModel.Map(user.RessourcesListWithAccess).ToArray(), long.Parse(user.Id)));
                    }
                    break;
            }
            return result;
        }
        

        private static Dictionary<string, byte[]> GetFilesForEmail(ClJoinFile[] existingFilesToJoin, List<ClJoinFile> newFilesToJoin)
        {
            var filesForEmail = existingFilesToJoin.ToDictionary(item => item.FileName, item => item.ByteFile);

            foreach (var item in newFilesToJoin)
            {
                filesForEmail.Add(item.FileName, item.ByteFile);
            }

            return filesForEmail;
        }


        private static async Task<bool> SaveComment(ServiceGateWayClient gatewayITask, TaskUser user, ClTaskDocument taskDocument, List<ClJoinFile> files, SendMessageViewModel model)
        {
            var clcomment = new ClComment
            {
                CurrentTask = new ClTaskDocument(),
                joinFiles = files.ToArray(),
                Confidential = model.IsConfidential ? 1 : 0,
                TaskStatus = model.Status,
                MailBodyIsHtml = true,
                MailBody = model.Message,
                AuthorMailComment = user.Email
            };
            Log.Write("SaveComment", "Conversation", model.TaskId.ToString(), 3, "debut AddComment");
            var ret = await Task.Run(() => gatewayITask.AddComment(taskDocument, clcomment, user.Id, "CONVERSATION"));
            Log.Write("SaveComment", "Conversation", model.TaskId.ToString(), 3, "fin AddComment");
            return ret;

        }

        private static async Task<bool> SaveInternMessage(ServiceGateWayClient gatewayITask, TaskUser user, ClTaskDocument taskDocument, List<ClJoinFile> files, SendMessageViewModel model)
        {
            var internalMail = new ClInternConversation
            {
                TaskStatus = model.Status,
                Subject = model.DefaultSubject + " " + model.CustomSubject ?? "",
                MailBody = model.Message,
                From = user.Email,
                Confidential = model.IsConfidential ? 1 : 0,
                CopyCc = FormatStringToArray(model.StringCopyToId),
                joinFiles = files.ToArray(),
                MailBodyIsHtml = true,
                Recipient = model.InternRecipientId
            };
            // RM 120977 - Message interne sans attribution: Erreur message de notification
            internalMail.AttributeTask = model.AssignToUser;

            //TODO il faudra garnir le paramètre qui précise si c'est un reply d'un message ou un nouveau message interne
            return await Task.Run(() => gatewayITask.AddInternConversation(taskDocument, internalMail, user.Id, false));
        }

        private static async Task<ResultViewModel> SaveExternMessage(ServiceGateWayClient gatewayITask, TaskUser user, ClTaskDocument taskDocument, List<ClJoinFile> files, Dictionary<string, byte[]> filesForEmail, SendMessageViewModel model)
        {
            
            //Mail destiné à l'extérieur
            //var head = "[##Task##] (" + EnvironmentHelper.Get_Label(environment.language, environment._listLabels,"LBL_NOT_DELETE_THIS", false, false) + ") <br/> ";
            //model.Message = head + " " + model.Message ;
            bool result = true;
            long employerNumber = model.EmployerNumber;
            long workerNumber = model.WorkerNumber;

            if (model.IsNewTask)
            {
                //change status to new when creating new task from draft
                if (model.TypeOfMessage == 8)
                    model.Status = 1;

                //création de la tâche avant de créer l'élément de conversation si c'est un email direct
                long taskId = 0;
                var taskNew = new TaskDocumentViewModel
                {
                    Task = new TaskViewModel
                    {
                        FormattedBeginDate = DateTime.MinValue.ToString("dd-MM-yyyy"),
                        Description = model.CustomSubject,
                        TechnicalId = model.TaskId,
                        Type = 5, //par défaut - traitement d'email
                        Priority = 2,
                        ExpectedEndDate = DateTime.Now,
                        BeginDate = DateTime.MinValue,
                        EmployerNumber = employerNumber,
                        WorkerNumber = workerNumber
                    },
                    Affectation = new AffectationViewModel
                    {
                        SelectedAgent = user.Id,
                        SelectedEntity = user.CurrentEntityIdTechnique.ToString()
                    },
                    Context = user.CurrentContext,
                    OrigineContext = user.CurrentOriginContext,
                    CurrentStatus = model.Status,

                };

                var mailTo = FormatStringToArrayWithoutDot(model.InternRecipient);
                // Si un seul employeur dans le To -> récupérer son n° d'employeur
                if (mailTo.Count() == 1 && employerNumber == 0)
                {
                    employerNumber = gatewayITask.GetEmployerFromEmail(mailTo[0]);
                    taskNew.Task.EmployerNumber = employerNumber;
                }

                // Mise à jour date début si nécessaire
                if (model.Status == 2)
                {
                    taskNew.Task.BeginDate = DateTime.Now;
                    taskNew.Task.FormattedBeginDate = DateTime.Now.ToString("dd-MM-yyyy");
                }
                // Mise à jour date fin si nécessaire
                if (model.Status == 4)
                {
                    if (employerNumber > 0)
                    {
                        taskNew.Task.EffectiveEndDate = DateTime.Now;
                        taskNew.Task.FormattedEffectiveEndDate = DateTime.Now.ToString("dd-MM-yyyy");
                    }
                    else
                    {
                        //changing to new when there is no employer
                        model.Status = 1;
                        taskNew.CurrentStatus = 1;
                    }
                }

                result = await Task.Run(() => gatewayITask.InsertTaskDocument(MapClTaskDocumentToTaskDocumentViewModel.Map(taskNew), user.Id, ref taskId, user.CurrentEntityNumber));
            }

            var externMail = new ClExternMail
            {
                Subject = model.DefaultSubject + " " + model.CustomSubject ?? "",
                MailBody = model.Message,
                Draft =  model.TypeOfMessage == 8,
                //MailBodyText = "je stocke du texte" + model.Message,
                From = user.Email,
                Recipient = FormatStringToArrayWithoutDot(model.InternRecipient),
                joinFiles = files.ToArray(),
                CopyCc = FormatStringToArray(model.StringCopyTo),
                MailBodyIsHtml = true

            };

            // Si le n° d'employeur trouvé est <> 0 et si nouvelle tâche
            // -> aller mettre ce n° d'employeur dans les pièces jointes
            // Si le n° d'employeur n'a pas déjà été garni
            if (employerNumber != 0 && model.IsNewTask)
            {
                foreach (var currentFile in externMail.joinFiles)
                {
                    if (currentFile.Document == null)
                    {
                        currentFile.Document = new ClDocument();

                        ClEmployerDetail currentEmployer = new ClEmployerDetail();
                        currentEmployer.EmployeurNumber = employerNumber;

                        currentFile.Document.EmployerList = new ClEmployerDetail[1];
                        currentFile.Document.EmployerList[0] = currentEmployer;

                        currentFile.WithIndexing = true;
                    }

                }
            }


            var conversationIdTechnic = await Task.Run(() => gatewayITask.AddExternMail(taskDocument, externMail, user.Id));

            if (model.TypeOfMessage == 8 && conversationIdTechnic != -1)
            {
                //if draft is created from a draft, the older draft is removed
                if (model.IsDraft && model.TaskContributeId > 0)
                   gatewayITask.DeleteConversation(model.TaskContributeId, user.IdAgent);

                return new ResultViewModel(){ NoError = true, Message1 = "Draft Created"};
            }


            // Lc le 5/8/2019 Le service donne l'id technique de l'élément de conversation 
            if (conversationIdTechnic != -1)
            {
                model.Recipient = externMail.Recipient;
                //model.CopyTo = externMail.CopyCc;
                model.Message = externMail.MailBody;

                //Récupérer tout les utilisateurs de Task
                //126928 - Le 'copie à' ne fonctionne pas
                List<ClAgent> allAgentsGroupsList = new List<ClAgent>();

                if (HttpContext.Current.Session["AllAgentsGroupsListTrueIntercept"] == null)
                {
                    allAgentsGroupsList = gatewayITask.GetAllTaskUser(true).ToList();
                    HttpContext.Current.Session["AllAgentsGroupsListTrueIntercept"] = allAgentsGroupsList;
                }
                else
                    allAgentsGroupsList =(List<ClAgent>) HttpContext.Current.Session["AllAgentsGroupsListTrueIntercept"];

                // Boucler sur les personnes en Cc et les retirer si ce sont des utilisateurs de Task
                List<string> CopyCcList = new List<string>();

                foreach (var currentCc in externMail.CopyCc)
                {
                    if (allAgentsGroupsList.Count(p => p.Mail.ToLower() == currentCc.ToLower()) == 0)
                        CopyCcList.Add(currentCc);
                }

                model.CopyTo = CopyCcList.ToArray();
                model.ConversationIdTech = conversationIdTechnic;
                model.UserId = user.Id;

                var mailReturn = await Task.Run(() => ManageMail.SendMailAndImage(model, user.Email, filesForEmail));
                //element de conversation bien crée et mail bien envoyé
                if (mailReturn)
                {
                    //delete the draft message after sending the mail without errors
                    if (model.IsDraft && model.TaskContributeId > 0)
                    {
                        var draftDeleted = gatewayITask.DeleteConversation(model.TaskContributeId, user.IdAgent);
                    }

                    return new ResultViewModel
                    {
                        NoError = mailReturn,
                        Message1 = ""
                    };
                }
                else
                {
                    //element de conversation bien crée et PROBLEME envoi mail
                    return new ResultViewModel
                    {
                        NoError = false,
                        Message1 = "ERROR_SEND_MAIL_AFTER"
                    };
                }

            }

            return new ResultViewModel
            {
                    NoError = false,
                    Message1 = "ERROR_OCCURD_RETRY_LATER"
            };
            
            
        }

        private static bool CanSeeMessage(ClConversation elem, TaskUser user, ServiceGateWayClient gatewayITask)
        {
            var recipientsTaskUser = new List<string>();
            if (elem.Confidential == 1)
            {
                var listrec = gatewayITask.GetAllTaskUser(false);
                //récupération des personnes qui ont reçu le message
                foreach (var rec in elem.Recipient)
                {
                    var elt = listrec.FirstOrDefault(a => a.HrIdTechnique.ToString() == rec);
                    if (elt != null)
                    {
                        recipientsTaskUser.Add(elt.HrIdTechnique.ToString());
                    }
                }
            }
            //remove draft messages from others
            if (elem.ContributionType == 8 && elem.AuthorAgent.HrIdTechnique.ToString() != user.Id)
            {
                return false;
            }

            if ((elem.Confidential == 0) || (elem.Confidential == 1 && (elem.AuthorAgent.HrIdTechnique.ToString() == user.Id || recipientsTaskUser.Contains(user.Id))))
            {
                return true;
            }
            return false;
        }

        private static string[] FormatStringToArray(string formatThis)
        {
            if (formatThis != null)
            {
                var getNumberElement = formatThis.Split(';').Distinct().ToArray();
                
                var formated = new string[getNumberElement.Length];
                for (int i = 0; i <= getNumberElement.Length - 1; i++)
                {
                    if (getNumberElement[i] != "" && getNumberElement[i] != null)
                    {
                        if (getNumberElement[i].Contains("["))
                        {
                            getNumberElement[i] = BetweenTwoCharacters(getNumberElement[i], "[", "]");
                        }

                        formated[i] = getNumberElement[i];
                    }
                }

                return formated.Where(c => c != null).ToArray();
            }
            return new string[0];
        }

        private static string[] FormatStringToArrayWithoutDot(string formatThis)
        {
            if (formatThis != null)
            {
                var formated = new string[1];
                if (formatThis.Contains("["))
                {
                    formatThis = BetweenTwoCharacters(formatThis, "[", "]");
                }
                formated[0] = formatThis;
                return formated;
            }
            return new string[0];
        }

        private static string BetweenTwoCharacters(string transl, string firstString, string lastString)
        {
            var STR = transl;

            var pos1 = STR.IndexOf(firstString, StringComparison.Ordinal) + firstString.Length;
            var pos2 = STR.IndexOf(lastString, StringComparison.Ordinal);

            var finalString = STR.Substring(pos1, pos2 - pos1);

            return finalString;
        }

        //[OBSOLETE] Utiliser CreateALiasFormated ou CreateChainOfAddressMailWithAlias dans le cas ou il y a un tableau/liste d'adr mail
        public static string GetFullNameMail(List<ClAgent> fullAgents, string adrMail)
        {
            var str = adrMail;
            long safeLong = 0;
            long.TryParse(adrMail, out safeLong);

            if (safeLong != 0)
            {
                //var agent = fullAgents.FirstOrDefault(item => item.Mail.Equals(adrMail.ToLower()) || item.HrIdTechnique == safeLong);

                //if (agent != null)
                //{
                //    str = agent.Name + " " + agent.FirstName + " [" + agent.Mail + "]";
                //}

                foreach (var item in fullAgents.Where(item => item.Mail.Equals(adrMail) || item.HrIdTechnique == safeLong))
                {
                    str = item.Name + " " + item.FirstName + " [" + item.Mail + "]";
                }
            }
            else
            {
                var agent = fullAgents.FirstOrDefault(item => item.Mail.Equals(adrMail.ToLower()));

                if(agent != null)
                {
                    str = agent.Name + " " + agent.FirstName + " [" + agent.Mail + "]";
                }
            }
            
            return str;
        }

        //[OBSOLETE] Utiliser CreateALiasFormated ou CreateChainOfAddressMailWithAlias dans le cas ou il y a un tableau/liste d'adr mail
        private static string GetFullNameMailSplitComma(List<ClAgent> fullAgents, string adrMail)
        {
            //var str = adrMail;
            var str = "";
            var adrCC = "";
            var loopCC = adrMail.Split(',');

            foreach (var lp in loopCC)
            {
                long safeLong = 0;
                long.TryParse(lp, out safeLong);

                if (safeLong != 0)
                {
                    foreach (var item in fullAgents.Where(item => item.Mail.Equals(lp) || item.HrIdTechnique == safeLong))
                    {
                        str = item.Name + " " + item.FirstName + " [" + item.Mail + "]";
                    }
                }
                else
                {
                    var agent = fullAgents.FirstOrDefault(item => item.Mail.Equals(lp));

                    if(agent !=null)
                    {
                        str = agent.Name + " " + agent.FirstName + " [" + agent.Mail + "]";
                    }
                    else
                    {
                        //Si l'agent n'est pas trouvé on met son adr mail pour eviter ","
                        str = lp;
                    }

                    //foreach (var item in fullAgents.Where(item => item.Mail.Equals(lp)))
                    //{
                    //    str = item.Name + " " + item.FirstName + " [" + item.Mail + "]";
                    //}
                }
                adrCC += str + ",";
                str = "";
            }

            return adrCC.Remove(adrCC.Length - 1);
        }

        public static string CreateALiasFormated(string adrMail,string alias)
        {
            string aliasFormated = "";

            if(String.Compare(adrMail.Trim().ToUpper(),alias.Trim().ToUpper())!=0)
            {
                aliasFormated = alias + "[" + adrMail + "]";
            }
            else
            {
                aliasFormated = adrMail;
            }

            return aliasFormated;
        }

        public static string CreateChainOfAddressMailWithAlias(string [] tabAlias, string [] tabAdrMail)
        {
            var chainOfAddressMail = "";
            if (tabAlias.Length > 0 && tabAdrMail.Length > 0)
            {

                for (int i = 0; tabAdrMail.Length > i; i++)
                {
                    chainOfAddressMail += CreateALiasFormated(tabAdrMail[i], tabAlias[i]) + ";";
                }

                /*if (chainOfAddressMail.Length > 0)
                {
                    //Retire la derniere virgule inutile
                    chainOfAddressMail=chainOfAddressMail.Remove(chainOfAddressMail.Length - 1);
                }*/
            }
            return chainOfAddressMail;
        }
    }
}