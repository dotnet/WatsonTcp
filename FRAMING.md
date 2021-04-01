# WatsonTcp Framing

WatsonTcp overcomes many of the challenges developers face when building TCP-based in applications.  One of the primary challenges is the need to build a messaging layer over the top of TCP to provide demarcation (boundaries for data useful to the application itself).  Since TCP is a bidirectional stream, simply reading from the stream isn't useful unless you know exactly how many bytes to read or unless you're able to derive the number of bytes you need to read.  This is the primary problem WatsonTcp solves.  The second major problem WatsonTcp solves is the problem of integrating TCP communication within an application.  Applications must be able to handle situations where disconnections occur intentionally or unintentionally on either side and know when to pass a fully-formed application-layer message to the application logic itself.  WatsonTcp solves this problem through simple callbacks and events.  

*Framing* is the primary topic of this document.  Framing can be thought of as a data wrapper, known and understood by both sender and receiver, that defines the boundary of what was sent by the sender and should be read by the receiver.  Stephen Cleary has an excellent summary on the problem of framing on his blog (see https://blog.stephencleary.com/2009/04/message-framing.html) that covers framing (and why it is necessary) far better than I could.  But generally speaking, just because a sender sends 128 bytes doesn't mean the receiver knows how many bytes to read.  With framing, those 128 bytes would be encapsulated or prepended with some information that would convey to the receiver that it should read 128 bytes.

WatsonTcp implements a framing layer that is a hybrid of the two models mentioned in Mr. Cleary's blog.  Each message sent using WatsonTcp includes metadata (information about the data being sent) and the data (what is actually sent and what is actually useful to the receiver).  

The first model described in Mr. Cleary's blog is *length prefixing*.  This is the practice of prepending the number of bytes included in the message payload ahead of the payload itself.  For instance, sending ```Hello, world!``` with a length prefix could look like ```13:Hello, world!```, where the ```13``` indicates the number of bytes, the colon character ```:``` acts as a separator between length prefix and data, and the rest is the original data.  When the receiver receives this message, it would simply follow this pseudo-code:

- Create an empty byte array ```buffer```
- Loop
  - Read one byte ```b``` from the socket 
  - If ```b``` != ```:``` append to ```buffer```
- While character read is not ```:```
- Convert ```buffer``` contents to an integer ```len```
- Read ```len``` bytes from the network as ```data```

The second model is *delimeters*.  A delimeter is a set of characters understood by both sender and receiver to indicate the end of a message.  For example, the colon character ```:``` could act as a delimeter to instruct the receiver that it has reached the end of the message.  For instance, ```Hello, world!``` would be sent as ```Hello, world!:```.  The receiver would know to stop reading once the colon character ```:``` was reached.

The delimeter model, on its own, introduces serious challenges from a development and integration perspective.

- What if I want to send the delimeter character as part of a message?
- What if I read too many bytes and the delimeter is in the middle of what was read rather than at the end?  
- I don't want to read one byte at a time and continually check the tail of the buffer for the delimeter

WatsonTcp uses a hybrid of these two models that loosely follows the framing model used by HTTP.  I say *loosely* because where HTTP uses simple strings with newlines separating them for headers, WatsonTcp encapsulates all header metadata into a JSON object.  

## HTTP
```
Content-Type: text/plain[\r\n]
Content-Length: 1234[\r\n]
Authorization: hello@world.com[\r\n]
... other fields ...[\r\n]
[\r\n]
[data]
```

## WatsonTcp
```
{"len":1234,"s":"Normal",...other fields...}[\r\n]
[\r\n]
[data]
```

As you can see in examples above, the content length (i.e. length prefixing) is contained in the header's ```ContentLength``` parameter, and a delimeter exists between metadata and data (a carriage return and newline followed by another carriage return and newline, i.e. [\r\n\r\n]).

## What Changed?

In versions of WatsonTcp prior to 4.0, a much more complicated framing model was used that resembled the following:
- The number of bytes for the message headers and payload ```len```
- A colon as a demarcation between ```len``` and ```metadata```
- A byte array indicating which metadata fields (of fixed size) were present in ```metadata```
- Contents of each metadata field (of fixed size)
- Data

While this framing model was bandwidth efficient (fewer bytes consumed on the network) compared with the framing model in version 4.0, version 4.0 has several advantages:
- The code in v4.0 is much *much* cleaner
- It is now far easier for me to add metadata fields
- Integration with non-WatsonTcp endpoints is now far easier

## Integration with Non-WatsonTcp Endpoints

If you're building an application that will need to integrate with WatsonTcp, let me know!  Here are some basic guidelines to follow that should allow for a successful integration:

- Maintain a persistent TCP connection with WatsonTcp
- Create a header JSON object of the form: ```{"len":n,"s":"Normal"}``` where the ```len``` is the number of data bytes and ```s``` is the status of ```Normal```
- Include any other pertinent parameters as found in the ```WatsonMessage``` class
- Do NOT use pretty serialization of the header object (this introduces additional ```\r\n``` into the data)
- Encode the header object to bytes and send it, followed by ```\r\n\r\n```
- Send the data

