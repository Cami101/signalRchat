using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace csharp_isolated;

public class Functions
{
    private readonly ILogger<Functions> _logger;

    // TODO: make the input a factory?
    public Functions(ILogger<Functions> logger)
    {
        _logger = logger;
    }

    [Function("index")]
    public HttpResponseData GetWebPage([HttpTrigger(AuthorizationLevel.Anonymous)] HttpRequestData req)
    {
        _logger.LogInformation("GetWebPage: called");
        var response = req.CreateResponse(HttpStatusCode.OK);
        _logger.LogInformation($"GetWebPage: response: {response}");
        response.WriteString(File.ReadAllText("content/index.html"));
        _logger.LogInformation($"GetWebPage: response: {response}");
        response.Headers.Add("Content-Type", "text/html");
        _logger.LogInformation($"GetWebPage: response: {response}");
        return response;
    }

    [Function("Negotiate")]
    public string Negotiate([HttpTrigger(AuthorizationLevel.Anonymous)] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "%SignalRHub%", UserId = "{query.userid}")] string signalRConnectionInfo)
    {
        _logger.LogInformation("Executing negotiation.");
        // The serialization of the connection info object is done by the framework. It should be camel case. The SignalR client respects the camel case response only.
        return signalRConnectionInfo;
    }
    [Function("OnConnected")]
    [SignalROutput(HubName = "%SignalRHub%")]
    public SignalRMessageAction OnConnected([SignalRTrigger("%SignalRHub%", "connections", "connected")] SignalRInvocationContext invocationContext,
        [CosmosDBInput(
            databaseName: "%CosmosDBName%",           
            containerName: "Rooms",  
            Connection = "CosmosDbConnection")] IReadOnlyList<MyDocument> dbDocuments
    )
    {
        _logger.LogInformation("Fetching all messages from Cosmos DB...");
        var textMessages = new List<NewMessage>();

        // Iterate through each message
        foreach (var dbDoc in dbDocuments)
        {
            textMessages.Add(dbDoc.message);
        }
        var connectionId = invocationContext.ConnectionId;
        var returnObject = new SignalRMessageAction("newRoom")
        {
            ConnectionId = connectionId,
            Arguments = new object[] { textMessages }
        };
        return returnObject;
    }

    // [Function("GetAllRooms")]
    // [SignalROutput(HubName = "%SignalRHub%")]
    // public SignalRMessageAction GetAllRooms(
    //     [SignalRTrigger("%SignalRHub%", "connections", "connected")]SignalRInvocationContext invocationContext,
    //     [CosmosDBInput(
    //         databaseName: "%CosmosDBName%",           
    //         containerName: "Rooms",  
    //         Connection = "CosmosDbConnection",
    //         SqlQuery = "SELECT * FROM c")] IReadOnlyList<MyDocument> dbDocuments)
    // {
    //     _logger.LogInformation("Fetching all messages from Cosmos DB...");
    //     var textMessages = new List<NewMessage>();

    //     // Iterate through each message
    //     foreach (var dbDoc in dbDocuments)
    //     {
    //         textMessages.Add(dbDoc.message);
    //     }
    //     var connectionId = invocationContext.ConnectionId;
    //     var returnObject = new SignalRMessageAction("newRoom")
    //     {
    //         ConnectionId = connectionId,
    //         Arguments = new object[] { textMessages }
    //     };
    //     return returnObject;
    // }
    //this is stupid but i couldn't use partitionkey, doesnt work
    [Function("GetAllRoomMessages")]
    [SignalROutput(HubName = "%SignalRHub%")]
    public SignalRMessageAction GetAllRoomMessages(
        [SignalRTrigger("%SignalRHub%", "messages", "GetAllRoomMessages")] SignalRInvocationContext invocationContext,
        [CosmosDBInput(
            databaseName: "%CosmosDBName%",           
            containerName: "%CosmosDBMessageContainer%",  
            Connection = "CosmosDbConnection",
            SqlQuery = "SELECT * FROM c")] IReadOnlyList<MyDocument> dbDocuments,
        string groupname)
    {
        _logger.LogInformation($"Fetching all chat messages for group: {groupname} from Cosmos DB...");
        var textMessages = new List<NewMessage>();

        // Iterate through each message
        foreach (var dbDoc in dbDocuments)
        {
            _logger.LogInformation(dbDoc.message.Group);
            if (dbDoc.message.Group == groupname){
                textMessages.Add(dbDoc.message);
            }
        }

        var connectionId = invocationContext.ConnectionId;
        var returnObject = new SignalRMessageAction("newMessage")
        {
            ConnectionId = connectionId,
            Arguments = new object[] { textMessages }
        };

        return returnObject;
    }


    [Function("OnDisconnected")]
    [SignalROutput(HubName = "%SignalRHub%")]
    public void OnDisconnected([SignalRTrigger("%SignalRHub%", "connections", "disconnected")] SignalRInvocationContext invocationContext)
    {
        _logger.LogInformation($"{invocationContext.ConnectionId} has disconnected");
    }

    [Function("JoinGroup")]
    [SignalROutput(HubName = "%SignalRHub%")]
    public SignalRGroupAction JoinGroup([SignalRTrigger("%SignalRHub%", "messages", "JoinGroup", "connectionId", "groupName")] SignalRInvocationContext invocationContext, string connectionId, string groupName)
    {
        _logger.LogInformation($"joinRoom, roomName: {groupName}");
        return new SignalRGroupAction(SignalRGroupActionType.Add)
        {
            GroupName = groupName,
            ConnectionId = connectionId
        };
    }

    [Function("LeaveGroup")]
    [SignalROutput(HubName = "%SignalRHub%")]
    public SignalRGroupAction LeaveGroup([SignalRTrigger("%SignalRHub%", "messages", "LeaveGroup", "connectionId", "groupName")] SignalRInvocationContext invocationContext, string connectionId, string groupName)
    {
        _logger.LogInformation($"leaveRoom, roomName: {groupName}");
        return new SignalRGroupAction(SignalRGroupActionType.Remove)
        {
            GroupName = groupName,
            ConnectionId = connectionId
        };
    }

    [Function("createChatRoom")]
    public RoomResponse CreateChatRoom([SignalRTrigger("%SignalRHub%", "messages", "createChatRoom", "roomName")] SignalRInvocationContext invocationContext,
    string roomName)
    {
        _logger.LogInformation("CreateChatRoom: called");
        _logger.LogInformation($"CreateChatRoom, invocationContext: {invocationContext}");
        _logger.LogInformation($"CreateChatRoom, roomName: {roomName}");

        // Save the chat room to Cosmos DB or perform any other necessary operations
        return new RoomResponse()
        {
            Document = new MyDocument
            {
                id = System.Guid.NewGuid().ToString(),
                message = new NewMessage(invocationContext, roomName, "group")
            },
            HttpResponse = null
        };
    }


    [Function("postmessage")]
    public MessageResponse PostMessage([SignalRTrigger("%SignalRHub%", "messages", "postmessage", "message",  "groupName")] SignalRInvocationContext invocationContext,
    string message, string groupName)
    {
        // var logger = executionContext.GetLogger("HttpExample");
        _logger.LogInformation("PostMessage: called");
        _logger.LogInformation($"PostMessage, invocationContext: {invocationContext}");
        _logger.LogInformation($"PostMessage, message: {message}");
        return new MessageResponse()
        {
            Document = new MyDocument
            {
                id = System.Guid.NewGuid().ToString(),
                message = new NewMessage(invocationContext, message, groupName)
            },
            HttpResponse = null
        };
    }
    

    [Function("NewDatabaseMessage")]
    [SignalROutput(HubName = "%SignalRHub%")]
    public SignalRMessageAction NewDatabaseMessage([CosmosDBTrigger(
        databaseName: "%CosmosDBName%",
        containerName: "%CosmosDBMessageContainer%",
        Connection = "CosmosDbConnection",
        CreateLeaseContainerIfNotExists = true
    )] IReadOnlyList<MyDocument> dbDocuments)
    {
        _logger.LogInformation($"SignalRMessageAction, dbDocuments: {dbDocuments}");
        var dbDoc = dbDocuments.First();
        var groupName = dbDoc.message.Group;
        _logger.LogInformation($"SignalRMessageAction, dbDoc.id: {dbDoc.id}");
        _logger.LogInformation($"SignalRMessageAction, dbDoc.message: {dbDoc.message}");
        _logger.LogInformation($"SignalRMessageAction, dbDoc.Group: {groupName}");
        return new SignalRMessageAction("newMessage")
        {
            GroupName = groupName,
            Arguments = new object[] { new[] {dbDoc.message} }
        };
        // return null;
    }

    [Function("NewDatabaseRoom")]
    [SignalROutput(HubName = "%SignalRHub%")]
    public SignalRMessageAction NewDatabaseRoom([CosmosDBTrigger(
        databaseName: "%CosmosDBName%",
        containerName: "Rooms",
        Connection = "CosmosDbConnection",
        CreateLeaseContainerIfNotExists = true
    )] IReadOnlyList<MyDocument> dbDocuments)
    {
        _logger.LogInformation($"SignalRMessageAction, dbDocuments: {dbDocuments}");
        var dbDoc = dbDocuments.First();
        _logger.LogInformation($"SignalRMessageAction, dbDoc.id: {dbDoc.id}");
        _logger.LogInformation($"SignalRMessageAction, dbDoc.message: {dbDoc.message}");
        var returnObject = new SignalRMessageAction("newRoom")
        {
            Arguments = new object[] { new[] { dbDoc.message } } // array wrapper is needed to conform with get all chatrooms
        };

        // Log the return object manually
        _logger.LogInformation($"Returning SignalRMessageAction with Arguments: {string.Join(", ", returnObject.Arguments)}");

        return returnObject;
    }

    public class MessageResponse
    {
        [CosmosDBOutput("%CosmosDBName%", "%CosmosDBMessageContainer%",
            Connection = "CosmosDbConnection", CreateIfNotExists = true)]
        public MyDocument Document { get; set; }
        public HttpResponseData? HttpResponse { get; set; }
    }
    
    public class RoomResponse
    {
        [CosmosDBOutput("%CosmosDBName%", "Rooms",
            Connection = "CosmosDbConnection", CreateIfNotExists = true)]
        public MyDocument Document { get; set; }
        public HttpResponseData? HttpResponse { get; set; }
    }
    

    public class MyDocument {
        [JsonInclude]
        public string id { get; set; }
        [JsonInclude]
        public NewMessage message { get; set; }
    }

    // public class CosmosDbDocument {
    //     public string? id { get; set; }
    //     public object? message { get; set; }
    // }

    public class NewConnection
    {
        public string ConnectionId { get; }

        public string Authentication { get; }

        public NewConnection(string connectionId, string auth)
        {
            ConnectionId = connectionId;
            Authentication = auth;
        }
    }

    public class NewMessage
    {
        [JsonInclude]
        public string ConnectionId { get; set; }
        [JsonInclude]
        public string Sender { get; set; }
        [JsonInclude]
        public string Text { get; set; }
        [JsonInclude]
        public string Group { get; set; }

        [JsonConstructor]
        public NewMessage(string ConnectionId, string Sender, string Text, string Group)
        {
            this.ConnectionId = ConnectionId;
            this.Sender = Sender;
            this.Text = Text;
            this.Group = Group;
        }

        public NewMessage(SignalRInvocationContext invocationContext, string message, string group)
        {
            Sender = string.IsNullOrEmpty(invocationContext.UserId) ? string.Empty : invocationContext.UserId;
            ConnectionId = invocationContext.ConnectionId;
            Text = message;
            Group = group;
        }
    }

}