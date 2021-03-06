﻿using Microsoft.Bot.Connector;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using Microsoft.Azure; // Namespace for CloudConfigurationManager
using Microsoft.WindowsAzure.Storage; // Namespace for CloudStorageAccount
using Microsoft.WindowsAzure.Storage.Table; // Namespace for Table storage types

namespace BotSample.MessageRouting
{
    public class PartyAzureTable : TableEntity
    {
        public PartyAzureTable(string BotId, string ConversationId)
        {
            this.PartitionKey = BotId;
            this.RowKey = ConversationId;
        }

        public PartyAzureTable() { }

        public string Party { get; set; }

        public string Experation { get; set; }
    }

    /// <summary>
    /// Utility methods.
    /// </summary>
    public class MessagingUtils
    {
        public struct ConnectorClientAndMessageBundle
        {
            public ConnectorClient connectorClient;
            public IMessageActivity messageActivity;
        }

        /// <summary>
        /// Constructs a party instance using the sender (from) of the given activity.
        /// </summary>
        /// <param name="activity"></param>
        /// <returns>A newly created Party instance.</returns>
        public static Party CreateSenderParty(IActivity activity)
        {
            // Retrieve the storage account from the connection string.
            var azureTable = CreateTable("StorageConnectionString", "Conversations");

            var party = new Party(activity.ServiceUrl, activity.ChannelId, activity.From, activity.Conversation);
            AddPartyToAzureTable(azureTable, party);
            return party;
        }

        private static void AddPartyToAzureTable(CloudTable table, Party party)
        {
            var pAzure = new PartyAzureTable("InterruptBot", party.ConversationAccount.Id);
            pAzure.Party = JsonConvert.SerializeObject(party);
            pAzure.Experation = DateTime.Now.AddHours(1).ToUniversalTime().ToString();

            TableOperation insertOperation = TableOperation.InsertOrReplace(pAzure);
            table.Execute(insertOperation);
        }

        private static CloudTable CreateTable(string connection, string tableName)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting(connection));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Retrieve a reference to the table.
            CloudTable table = tableClient.GetTableReference(tableName);

            // Create the table if it doesn't exist.
            table.CreateIfNotExists();

            return table;
        }

        /// <summary>
        /// Constructs a party instance using the recipient of the given activity.
        /// </summary>
        /// <param name="activity"></param>
        /// <returns>A newly created Party instance.</returns>
        public static Party CreateRecipientParty(IActivity activity)
        {
            return new Party(activity.ServiceUrl, activity.ChannelId, activity.Recipient, activity.Conversation);
        }

        /// <summary>
        /// Replies to the given activity with the given message.
        /// </summary>
        /// <param name="activity">The activity to reply to.</param>
        /// <param name="message">The message.</param>
        public static async Task ReplyToActivityAsync(Activity activity, string message)
        {
            if (activity != null && !string.IsNullOrEmpty(message))
            {
                Activity replyActivity = activity.CreateReply(message);
                ConnectorClient connector = new ConnectorClient(new Uri(activity.ServiceUrl));
                await connector.Conversations.ReplyToActivityAsync(replyActivity);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Either the activity is null or the message is empty - Activity: {activity}; message: {message}");
            }
        }

        /// <summary>
        /// Creates a connector client with the given message activity for the given party as the
        /// recipient. If this party has an ID of a specific user (ChannelAccount is valid), then
        /// the that user is set as the recipient. Otherwise, the whole channel is addressed.
        /// </summary>
        /// <param name="serviceUrl">The service URL of the channel of the party to send the message to.</param>
        /// <param name="newMessageActivity">The message activity to send.</param>
        /// <param name="appId">The bot application ID (optional).</param>
        /// <param name="appPassword">The bot password (optional).</param>
        /// <returns>A bundle containing a newly created connector client (that is used to send
        /// the message and the message activity (the content of the message).</returns>
        public static ConnectorClientAndMessageBundle CreateConnectorClientAndMessageActivity(
            string serviceUrl, IMessageActivity newMessageActivity,
            string appId = null, string appPassword = null)
        {
            ConnectorClient newConnectorClient = null;

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appPassword))
            {
                newConnectorClient = new ConnectorClient(new Uri(serviceUrl));
            }
            else
            {
                newConnectorClient = new ConnectorClient(new Uri(serviceUrl), new MicrosoftAppCredentials(appId, appPassword));
            }

            ConnectorClientAndMessageBundle bundle = new ConnectorClientAndMessageBundle()
            {
                connectorClient = newConnectorClient,
                messageActivity = newMessageActivity
            };

            return bundle;
        }

        /// <summary>
        /// For convenience.
        /// </summary>
        /// <param name="partyToMessage">The party to send the message to.</param>
        /// <param name="messageText">The message text content.</param>
        /// <param name="senderAccount">The channel account of the sender.</param>
        /// <param name="appId">The bot application ID (optional).</param>
        /// <param name="appPassword">The bot password (optional).</param>
        /// <returns>A bundle containing a newly created connector client (that is used to send
        /// the message and the message activity (the content of the message).</returns>
        public static ConnectorClientAndMessageBundle CreateConnectorClientAndMessageActivity(
            Party partyToMessage, string messageText, ChannelAccount senderAccount,
            string appId = null, string appPassword = null)
        {
            IMessageActivity newMessageActivity = Activity.CreateMessageActivity();
            newMessageActivity.Conversation = partyToMessage.ConversationAccount;
            newMessageActivity.Text = messageText;

            if (senderAccount != null)
            {
                newMessageActivity.From = senderAccount;
            }

            if (partyToMessage.ChannelAccount != null)
            {
                newMessageActivity.Recipient = partyToMessage.ChannelAccount;
            }

            return CreateConnectorClientAndMessageActivity(partyToMessage.ServiceUrl, newMessageActivity, appId, appPassword);
        }

        /// <summary>
        /// Strips the mentions from the message text.
        /// </summary>
        /// <param name="messageActivity"></param>
        /// <returns>The stripped message.</returns>
        public static string StripMentionsFromMessage(IMessageActivity messageActivity)
        {
            string strippedMessage = messageActivity.Text;

            if (!string.IsNullOrEmpty(strippedMessage))
            {
                Mention[] mentions = messageActivity.GetMentions();

                foreach (Mention mention in mentions)
                {
                    string mentionText = mention.Text;

                    if (!string.IsNullOrEmpty(mentionText))
                    {
                        while (strippedMessage.Contains(mentionText))
                        {
                            strippedMessage = strippedMessage.Remove(
                                strippedMessage.IndexOf(mentionText), mentionText.Length);
                        }

                    }
                }

                strippedMessage = strippedMessage.Trim();
            }

            return strippedMessage;
        }

        /// <summary>
        /// Checks whether the resource response was OK or not.
        /// </summary>
        /// <param name="resourceResponse">The resource response to check.</param>
        /// <returns>True, if the response was OK (e.g. message went through). False otherwise.</returns>
        public static bool WasSuccessful(ResourceResponse resourceResponse)
        {
            // TODO Check the ID too once we've fixed the gateway error that occurs in message relaying
            return (resourceResponse != null /*&& resourceResponse.Id != null*/);
        }
    }
}