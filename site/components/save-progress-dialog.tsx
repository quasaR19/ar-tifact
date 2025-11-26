"use client";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { CheckCircle2, Loader2, XCircle } from "lucide-react";

export type SaveStepStatus = "pending" | "processing" | "success" | "error";

export interface SaveStep {
  id: string;
  label: string;
  status: SaveStepStatus;
  error?: string;
  details?: string;
}

interface SaveProgressDialogProps {
  open: boolean;
  steps: SaveStep[];
  overallStatus: "saving" | "success" | "error";
  onClose?: () => void;
}

export function SaveProgressDialog({
  open,
  steps,
  overallStatus,
  onClose,
}: SaveProgressDialogProps) {
  const getStatusIcon = (status: SaveStepStatus) => {
    switch (status) {
      case "success":
        return <CheckCircle2 className="h-5 w-5 text-green-500" />;
      case "error":
        return <XCircle className="h-5 w-5 text-red-500" />;
      case "processing":
        return <Loader2 className="h-5 w-5 text-blue-500 animate-spin" />;
      default:
        return (
          <div className="h-5 w-5 rounded-full border-2 border-muted-foreground/30" />
        );
    }
  };

  const getStatusText = (status: SaveStepStatus) => {
    switch (status) {
      case "success":
        return "Готово";
      case "error":
        return "Ошибка";
      case "processing":
        return "В процессе...";
      default:
        return "Ожидание";
    }
  };

  const getOverallTitle = () => {
    switch (overallStatus) {
      case "success":
        return "Сохранение завершено";
      case "error":
        return "Ошибка сохранения";
      default:
        return "Сохранение артефакта";
    }
  };

  const getOverallDescription = () => {
    switch (overallStatus) {
      case "success":
        return "Все данные успешно сохранены";
      case "error":
        return "Произошла ошибка при сохранении";
      default:
        return "Пожалуйста, подождите, идет сохранение...";
    }
  };

  const completedSteps = steps.filter((s) => s.status === "success").length;
  const totalSteps = steps.length;
  const progress = totalSteps > 0 ? (completedSteps / totalSteps) * 100 : 0;

  return (
    <Dialog open={open} onOpenChange={(open) => !open && onClose?.()}>
      <DialogContent className="max-w-2xl max-h-[80vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            {overallStatus === "saving" && (
              <Loader2 className="h-5 w-5 animate-spin text-primary" />
            )}
            {overallStatus === "success" && (
              <CheckCircle2 className="h-5 w-5 text-green-500" />
            )}
            {overallStatus === "error" && (
              <XCircle className="h-5 w-5 text-red-500" />
            )}
            {getOverallTitle()}
          </DialogTitle>
          <DialogDescription>{getOverallDescription()}</DialogDescription>
        </DialogHeader>

        {overallStatus === "saving" && totalSteps > 0 && (
          <div className="space-y-2">
            <div className="flex justify-between text-sm text-muted-foreground">
              <span>
                Шаг {completedSteps} из {totalSteps}
              </span>
              <span>{Math.round(progress)}%</span>
            </div>
            <div className="w-full bg-muted rounded-full h-2">
              <div
                className="bg-primary h-2 rounded-full transition-all duration-300"
                style={{ width: `${progress}%` }}
              />
            </div>
          </div>
        )}

        <div className="space-y-3 mt-4">
          {steps.length === 0 ? (
            <div className="flex items-center gap-3 p-3 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
              <span>Подготовка к сохранению...</span>
            </div>
          ) : (
            steps.map((step) => (
              <div
                key={step.id}
                className={cn(
                  "flex items-start gap-3 p-3 rounded-lg border transition-colors",
                  step.status === "error" && "border-red-500/50 bg-red-500/5",
                  step.status === "success" &&
                    "border-green-500/50 bg-green-500/5",
                  step.status === "processing" &&
                    "border-blue-500/50 bg-blue-500/5"
                )}
              >
                <div className="mt-0.5">{getStatusIcon(step.status)}</div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between gap-2">
                    <p className="font-medium text-sm text-muted-foreground">
                      {step.label}
                    </p>
                    <span
                      className={cn(
                        "text-xs",
                        step.status === "error" && "text-red-500",
                        step.status === "success" && "text-green-500",
                        step.status === "processing" && "text-blue-500",
                        step.status === "pending" && "text-muted-foreground"
                      )}
                    >
                      {getStatusText(step.status)}
                    </span>
                  </div>
                  {step.details && (
                    <p className="mt-1 text-xs text-muted">{step.details}</p>
                  )}
                  {step.error && (
                    <p className="mt-1 text-xs text-red-500">{step.error}</p>
                  )}
                </div>
              </div>
            ))
          )}
        </div>

        {overallStatus !== "saving" && (
          <div className="flex justify-end gap-2 mt-4 pt-4 border-t">
            <button
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium rounded-md border border-input bg-background hover:bg-accent hover:text-accent-foreground transition-colors"
            >
              {overallStatus === "success" ? "Закрыть" : "Понятно"}
            </button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
