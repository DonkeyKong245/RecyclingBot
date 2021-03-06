﻿using System.Threading;
using System.Threading.Tasks;
using RecyclingBot.Control.Common;
using RecyclingBot.Control.Common.Photo;
using RecyclingBot.Control.Handlers.RecyclingCodeRecognition.CodeRecognition;
using RecyclingBot.Control.Handlers.RecyclingCodeRecognition.RecyclingCodeInfo;
using RecyclingBot.Control.Handlers.Wiki.FractionsInfo;
using RecyclingBot.Control.Handlers.Wiki.FractionsInfo.Info;
using Telegram.Bot.Framework.Abstractions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace RecyclingBot.Control.Handlers.RecyclingCodeRecognition
{
  public class PhotoWithRecyclingCodeHandler : IUpdateHandler
  {
    private readonly CodeRecognitionModelController _codeRecognitionModelController;

    public PhotoWithRecyclingCodeHandler(CodeRecognitionModelController codeRecognitionModelController)
    {
      _codeRecognitionModelController = codeRecognitionModelController;
    }

    public static bool CanHandle(IUpdateContext context)
    {
      UpdateType updateType = context?.Update?.Type ?? UpdateType.Unknown;

      if (updateType == UpdateType.Message)
      {
        bool hasPhoto = (context?.Update?.Message?.Photo?.Length ?? 0) > 0;
        if (hasPhoto)
        {
          long chatId = context?.Update?.Message?.Chat?.Id ?? -1;
          return RecyclingCodeFromPhotoHandler.ChatIdsAwaitingForPhoto.Contains(chatId);
        }
      }

      return false;
    }

    public async Task HandleAsync(IUpdateContext context, UpdateDelegate next, CancellationToken cancellationToken)
    {
      long chatId = context?.Update?.Message?.Chat?.Id ?? -1;
      RecyclingCodeFromPhotoHandler.ChatIdsAwaitingForPhoto.Remove(chatId);

      bool canProcessMessage = _codeRecognitionModelController?.CanProcessImage ?? false;
      if (!canProcessMessage)
      {
        await context.Bot.Client.SendTextMessageAsync(
          chatId: context.Update.Message.Chat.Id,
          text: "Can't process photos"
        );

        return;
      }

      using (PhotoHandler photoHandler = await PhotoHandler.FromMessage(context, context?.Update?.Message))
      {
        if (photoHandler == null)
        {
          await context.Bot.Client.SendTextMessageAsync(
            chatId: context.Update.Message.Chat.Id,
            text: "Can't download photo"
          );

          return;
        }

        ImageData imageData = new ImageData
        {
          ImagePath = photoHandler.FilePath
        };

        CodeRecognitionResult codeRecognitionResult = _codeRecognitionModelController.Recognize(imageData);
        if (codeRecognitionResult == null || !codeRecognitionResult.SuccessfullyProcessedImage)
        {
          await context.Bot.Client.SendTextMessageAsync(
            chatId: context.Update.Message.Chat.Id,
            text: "Failed code recognition"
          );

          return;
        }

        IRecyclingCodeInfo recyclingCodeInfo;
        if (!RecyclingCodeInfoWiki.LabelToFractionInfo.TryGetValue(codeRecognitionResult.PredictedLabel, out recyclingCodeInfo))
        {
          await context.Bot.Client.SendTextMessageAsync(
            chatId: context.Update.Message.Chat.Id,
            text: "Unknown recycling code recognized"
          );

          return;
        }

        foreach (string recyclingCodeInfoItem in RecyclingCodeRecognitionHandler.FormatRecyclingCodeInfo(recyclingCodeInfo))
        {
          await context.Bot.Client.SendTextMessageAsync(
            chatId: chatId,
            text: recyclingCodeInfoItem
          );
        }
      }
    }
  }
}
