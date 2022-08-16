﻿using Dapr.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SendGrid;
using SendGrid.Helpers.Mail;
using TasksTracker.Processor.Backend.Svc.Models;

namespace TasksTracker.Processor.Backend.Svc.Controllers
{
    [Route("api/tasksnotifier")]
    [ApiController]
    public class TasksNotifierController : ControllerBase
    {
         private static string STORE_NAME = "emaillogsstatestore";
        private readonly IConfiguration _config;
        private readonly ILogger _logger;

              private readonly DaprClient _daprClient;

        public TasksNotifierController(IConfiguration config, ILogger<TasksNotifierController> logger, DaprClient daprClient)
        {
            _config = config;
            _logger = logger;
             _daprClient = daprClient;
        }

        public IActionResult Get()
        {
            return Ok();
        }

        [Dapr.Topic("dapr-pubsub-servicebus", "tasksavedtopic")]
        [HttpPost("tasksaved")]
        public async Task<IActionResult> TaskSaved([FromBody] TaskModel taskModel)
        {
            _logger.LogInformation("Started processing message with Task Name '{0}'", taskModel.TaskName);

            var sendGridResponse = await SendEmail(taskModel);

            if (sendGridResponse.Item1)
            {
                return Ok($"SendGrid response staus code: {sendGridResponse.Item1}");
            }

            return BadRequest($"Failed to send email, SendGrid response status code: {sendGridResponse.Item1}");
        }

        private async Task<Tuple<bool, string>> SendEmail(TaskModel taskModel)
        {

            var apiKey = _config.GetValue<string>("SendGrid:ApiKey");
            var client = new SendGridClient(apiKey);
            var from = new EmailAddress("taiseer.joudeh@gmail.com", "Tasks Tracker Notification");
            var subject = $"Task '{taskModel.TaskName}' is assigned to you!";
            var to = new EmailAddress(taskModel.TaskAssignedTo, taskModel.TaskAssignedTo);
            var plainTextContent = $"Task '{taskModel.TaskName}' is assigned to you. Task should be completed by the end of: {taskModel.TaskDueDate.ToString("dd/MM/yyyy")}";
            var htmlContent = plainTextContent;
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
            var response = await client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email with subject '{0}' sent to: '{1}' successfuly", subject, taskModel.TaskAssignedTo);

                await _daprClient.SaveStateAsync(STORE_NAME, 
                                                        $"{taskModel.TaskId.ToString()}_{taskModel.TaskAssignedTo}", 
                                                        new EmailLogModel() {EmailTo = to.Email, EmailContent = plainTextContent});

                 _logger.LogInformation("Email log for task with id: {0} saved successfuly", taskModel.TaskId);
            }
            else
            {
                _logger.LogWarning("Failed to send email with subject '{0}' To: '{1}'. Status Code: {2}", subject, taskModel.TaskAssignedTo, response.StatusCode);
            }

            return new Tuple<bool, string>(response.IsSuccessStatusCode, response.StatusCode.ToString());

        }
    }

    public class EmailLogModel
    {
        public string EmailTo { get; set; } = string.Empty;
        public string EmailContent { get; set; } = string.Empty;
    }

}
