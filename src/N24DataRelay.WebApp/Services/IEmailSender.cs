// Re-export types from Core so WebApp code can use them without extra usings.
global using IAuditLogger = N24DataRelay.Core.Interfaces.IAuditLogger;
global using AuditEventTypes = N24DataRelay.Core.Models.AuditEventTypes;
global using IEmailSender = N24DataRelay.Core.Interfaces.IEmailSender;
