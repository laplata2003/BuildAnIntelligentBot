﻿
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Prompts;
using Microsoft.Recognizers.Text;
using ConfirmPrompt = Microsoft.Bot.Builder.Dialogs.ConfirmPrompt;
using TextPrompt = Microsoft.Bot.Builder.Dialogs.TextPrompt;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Bot;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Builder.Core.Extensions;
using Microsoft.Extensions.Options;
using ChatBot.Models;
using ChatBot.Services;
using Microsoft.Bot.Builder.Ai.LUIS;

namespace ChatBot
{

    public static class PromptStep
    {
        public const string GatherInfo = "gatherInfo";
        public const string TimePrompt = "timePrompt";
        public const string AmountPeoplePrompt = "amountPeoplePrompt";
        public const string NamePrompt = "namePrompt";
        public const string ConfirmationPrompt = "confirmationPrompt";
    }


    public class EchoBot : IBot
    {

        private readonly DialogSet _dialogs;

        public EchoBot(IOptions<MySettings> config)
        {
            _dialogs = new DialogSet();
            _dialogs.Add(PromptStep.TimePrompt, new TextPrompt());
            _dialogs.Add(PromptStep.AmountPeoplePrompt, new TextPrompt(AmountPeopleValidator));
            _dialogs.Add(PromptStep.NamePrompt, new TextPrompt());
            _dialogs.Add(PromptStep.ConfirmationPrompt, new ConfirmPrompt(Culture.English));
            _dialogs.Add(PromptStep.GatherInfo, new WaterfallStep[] { FinalStep });
//            _dialogs.Add(PromptStep.GatherInfo, new WaterfallStep[] { TimeStep, AmountPeopleStep, NameStep, ConfirmationStep, FinalStep });
        }

        /// <summary>
        /// Every Conversation turn for our EchoBot will call this method. In here
        /// the bot checks the Activty type to verify it's a message, bumps the 
        /// turn conversation 'Turn' count, and then echoes the users typing
        /// back to them. 
        /// </summary>
        /// <param name="context">Turn scoped context containing all the data needed
        /// for processing this conversation turn. </param>        
        public async Task OnTurn(ITurnContext context)
        {
            var state = context.GetConversationState<ReservationData>();
            var dialogContext = _dialogs.CreateContext(context, state);
            await dialogContext.Continue();

            // This bot is only handling Messages
            if (context.Activity.Type == ActivityTypes.Message)
            {
                if (!context.Responded)
                {
                    var result = context.Services.Get<RecognizerResult>(LuisRecognizerMiddleware.LuisRecognizerResultKey);
                    var topIntent = result?.GetTopScoringIntent();

                    switch (topIntent != null ? topIntent.Value.intent : null)
                    {
                        case "TodaysSpecialty":
                            //await context.SendActivity($"For today we have the following options: {string.Join(", ", BotConstants.Specialties)}");
                            await TodaysSpecialtiesHandler(context);
                            break;

                        case "ReserveTable":
                            var amountPeople = result.Entities["AmountPeople"] != null ? (string)result.Entities["AmountPeople"]?.First : null;
                            var time = GetTimeValueFromResult(result);
                            ReservationHandler(dialogContext, amountPeople, time);
                            break;

                        default:
                            await context.SendActivity("Sorry, I didn't understand that.");
                            break;
                    }
                }
            }
            else if (context.Activity.Type == ActivityTypes.ConversationUpdate && context.Activity.MembersAdded.FirstOrDefault()?.Id == context.Activity.Recipient.Id)
            {
                var msg = "Hi! I'm a restaurant assistant bot. I can help you with your reservation.";
                await context.SendActivity(msg);
            }
        }

        private async Task TodaysSpecialtiesHandler(ITurnContext context)
        {
            var actions = new[]
            {
                new CardAction(type: ActionTypes.ShowImage, title: "Carbonara", value: "Carbonara", image: $"{BotConstants.Site}/carbonara.jpg"),
                new CardAction(type: ActionTypes.ShowImage, title: "Pizza", value: "Pizza", image: $"{BotConstants.Site}/pizza.jpg"),
                new CardAction(type: ActionTypes.ShowImage, title: "Lasagna", value: "Lasagna", image: $"{BotConstants.Site}/lasagna.jpg")
            };

            var cards = actions
              .Select(x => new HeroCard
              {
                  Images = new List<CardImage> { new CardImage(x.Image) },
                  Buttons = new List<CardAction> { x }
              }.ToAttachment())
              .ToList();
            var activity = (Activity)MessageFactory.Carousel(cards, "For today we have:");

            await context.SendActivity(activity);
        }

        private string GetTimeValueFromResult(RecognizerResult result)
        {
            var timex = (string)result.Entities["datetime"]?.First["timex"].First;
            if (timex != null)
            {
                timex = timex.Contains(":") ? timex : $"{timex}:00";
                return DateTime.Parse(timex).ToString("MMMM dd \\a\\t HH:mm tt");
            }

            return null;
        }

        private async void ReservationHandler(DialogContext dialogContext, string amountPeople, string time)
        {
            var state = dialogContext.Context.GetConversationState<ReservationData>();
            state.AmountPeople = amountPeople;
            state.Time = time;
            await dialogContext.Begin(PromptStep.GatherInfo);
        }

        private async Task FinalStep(DialogContext dialogContext, object result, SkipStepFunction next)
        {
            var state = dialogContext.Context.GetConversationState<ReservationData>();
            if (result != null)
            {
                var confirmation = (result as ConfirmResult).Confirmation;
                string msg = null;
                if (confirmation)
                {
                    msg = $"Great, we will be expecting you this {state.Time}. Thanks for your reservation {state.FirstName}!";
                }
                else
                {
                    msg = "Thanks for using the Contoso Assistance. See you soon!";
                }

                await dialogContext.Context.SendActivity(msg);
            }

            await dialogContext.End(state);
        }

        private async Task AmountPeopleValidator(ITurnContext context, TextResult result)
        {
            if (!int.TryParse(result.Value, out int numberPeople))
            {
                result.Status = PromptStatus.NotRecognized;
                var msg = "The amount of people should be a number.";
                await context.SendActivity(msg);
            }
        }



    }    
}
