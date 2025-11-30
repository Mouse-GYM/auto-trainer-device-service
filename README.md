# Autotrainer API: Local API Service

The local API service is a bridge between the core acquisition application and "remote" clients (where remote
could include a local web view that is not the core application user interface).

The messaging interfaces on the core application (via the Python `auto-trainer-api` package) are not confined to the
local machine.  If a central service is used to proxy for multiple devices, running this service may be optional.

The intent of the service is to be lightweight, support containerization for simple deployment with other
support infrastructure, and provide services via standard interfaces (REST, GraphQL, WebSocket, etc.).

A number of common options are potentially well-suited for this task including FastApi/Flask (Python),
node.js (JavaScript), ASP.NET Core (C#), and others.

Given the lightweight nature of this service, making a change at later time should be relatively easy.  The current
choice is use ASP.NET Core with C# due to:

* The small number of 3rd-party dependencies needed for core functionality as compared to node and many Python frameworks
* Smaller containerization profile (depending on choices in other frameworks)
* Simple integration and support for long-running background services
* Type safety and other language features

Some of these elements are less applicable with the most recent versions of the other languages and frameworks at the
time this service originated. However, requiring those most recent versions is a constraint that is difficult or not
possible to meet on the target hardware at this time.
