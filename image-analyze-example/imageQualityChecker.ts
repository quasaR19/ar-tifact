/**
 * Интерфейс для результата проверки качества изображения
 * Основан на требованиях ARCore для Augmented Images
 */
export interface ImageQualityResult {
  score: number; // Оценка качества от 0 до 100 (ARCore рекомендует минимум 75)
  resolution: string; // Разрешение в формате "1920x1080"
  fileSize: string; // Размер файла в читаемом формате
  format: string; // Формат изображения
  aspectRatio: string; // Соотношение сторон
  qualityLabel: string; // Текстовое описание качества
  qualityClass: "poor" | "good" | "excellent"; // CSS класс для стилизации
  meetsMinimumRequirements: boolean; // Соответствует ли минимальным требованиям ARCore (300x300)
  meetsFileSizeRequirement: boolean; // Соответствует ли минимальному размеру файла (50KB)
  recommendations: string[]; // Рекомендации по улучшению
}

/**
 * Проверяет качество изображения на основе различных характеристик
 * @param file - Файл изображения
 * @returns Promise с результатом проверки качества
 */
export async function checkImageQuality(
  file: File
): Promise<ImageQualityResult> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    reader.onload = (e) => {
      const img = new Image();

      img.onload = () => {
        try {
          const result = analyzeImage(img, file);
          resolve(result);
        } catch (error) {
          reject(error);
        }
      };

      img.onerror = () => {
        reject(new Error("Не удалось загрузить изображение"));
      };

      if (e.target?.result) {
        img.src = e.target.result as string;
      }
    };

    reader.onerror = () => {
      reject(new Error("Не удалось прочитать файл"));
    };

    reader.readAsDataURL(file);
  });
}

/**
 * Анализирует изображение и вычисляет оценку качества
 * На основе требований ARCore для Augmented Images
 */
function analyzeImage(img: HTMLImageElement, file: File): ImageQualityResult {
  const width = img.width;
  const height = img.height;
  const fileSize = file.size;
  const format = getImageFormat(file.name);

  // ARCore требования: минимум 300x300 пикселей
  const MIN_RESOLUTION = 300;
  const meetsMinimumRequirements =
    width >= MIN_RESOLUTION && height >= MIN_RESOLUTION;

  // Минимальный размер файла для ARCore (50KB)
  // Файлы меньше этого размера обычно имеют сильное сжатие и не работают в ARCore
  const MIN_FILE_SIZE = 50 * 1024; // 50KB в байтах
  const meetsFileSizeRequirement = fileSize >= MIN_FILE_SIZE;

  const recommendations: string[] = [];

  // Проверка минимального разрешения
  if (!meetsMinimumRequirements) {
    recommendations.push(
      `Разрешение должно быть не менее ${MIN_RESOLUTION}x${MIN_RESOLUTION} пикселей (ARCore требование)`
    );
  }

  // Проверка минимального размера файла (жесткое ограничение)
  if (!meetsFileSizeRequirement) {
    recommendations.push(
      `Размер файла должен быть не менее ${formatFileSize(
        MIN_FILE_SIZE
      )} (ARCore требование). Слишком маленький файл указывает на сильное сжатие, что делает изображение непригодным для ARCore.`
    );
  }

  // Проверка формата
  if (format !== "png" && format !== "jpg") {
    recommendations.push("Рекомендуется использовать формат PNG или JPEG");
  }

  // Вычисляем оценку качества на основе различных факторов
  let score = 0;

  // 1. Оценка по разрешению (максимум 35 баллов)
  // ARCore: минимум 300x300, большее разрешение не обязательно улучшает производительность
  const resolutionScore = calculateResolutionScore(width, height);
  score += resolutionScore;

  // 2. Оценка по размеру файла (максимум 20 баллов)
  // Избегаем сильного сжатия
  const fileSizeScore = calculateFileSizeScore(fileSize, width, height);
  score += fileSizeScore;

  // 3. Оценка по соотношению сторон (максимум 20 баллов)
  const aspectRatioScore = calculateAspectRatioScore(width, height);
  score += aspectRatioScore;

  // 4. Оценка по формату (максимум 15 баллов)
  const formatScore = calculateFormatScore(format);
  score += formatScore;

  // 5. Бонус за соответствие минимальным требованиям (10 баллов)
  if (meetsMinimumRequirements) {
    score += 10;
  }

  // 6. Жесткое ограничение: если файл слишком маленький, сильно снижаем оценку
  // Файлы меньше 50KB с высокой вероятностью не будут работать в ARCore
  if (!meetsFileSizeRequirement) {
    // Снижаем оценку на 30 баллов (критическое ограничение)
    score = Math.max(0, score - 30);
  }

  // Ограничиваем оценку до 100
  score = Math.min(100, Math.max(0, Math.round(score)));

  // Определяем категорию качества на основе рекомендаций ARCore
  // ARCore рекомендует минимум 75 баллов для надежного обнаружения
  let qualityLabel: string;
  let qualityClass: "poor" | "good" | "excellent";

  if (score < 75) {
    qualityLabel = "Не рекомендуется для ARCore (минимум 75)";
    qualityClass = "poor";
    if (score >= 50) {
      recommendations.push(
        "Оценка ниже рекомендуемого минимума 75. Рассмотрите использование другого изображения."
      );
    }
  } else if (score >= 75 && score < 90) {
    qualityLabel = "Хорошее качество для ARCore";
    qualityClass = "good";
  } else {
    qualityLabel = "Отличное качество для ARCore";
    qualityClass = "excellent";
  }

  // Дополнительные рекомендации (не критичные)
  if (width > 2000 || height > 2000) {
    recommendations.push(
      "Высокое разрешение не обязательно улучшает производительность ARCore"
    );
  }

  return {
    score,
    resolution: `${width}x${height}`,
    fileSize: formatFileSize(fileSize),
    format: format.toUpperCase(),
    aspectRatio: calculateAspectRatio(width, height),
    qualityLabel,
    qualityClass,
    meetsMinimumRequirements,
    meetsFileSizeRequirement,
    recommendations,
  };
}

/**
 * Вычисляет оценку на основе разрешения изображения
 * ARCore: минимум 300x300, большее разрешение не обязательно лучше
 */
function calculateResolutionScore(width: number, height: number): number {
  const MIN_RESOLUTION = 300;

  // Проверка минимального требования ARCore
  if (width < MIN_RESOLUTION || height < MIN_RESOLUTION) {
    return 0; // Не соответствует минимальным требованиям
  }

  const megapixels = (width * height) / 1000000;

  // ARCore: оптимально около 0.09-0.5 MP (300x300 до ~700x700)
  // Большее разрешение не обязательно улучшает производительность
  if (megapixels >= 0.09 && megapixels <= 0.5) return 35; // Оптимальный диапазон
  if (megapixels >= 0.5 && megapixels <= 2) return 30; // Хорошо
  if (megapixels >= 2 && megapixels <= 4) return 25; // Приемлемо
  if (megapixels >= 4) return 20; // Выше оптимального, но работает
  return 15; // Минимально допустимо
}

/**
 * Вычисляет оценку на основе размера файла
 * Избегаем сильного сжатия (маленький файл = плохое качество)
 * Жесткое ограничение: файлы меньше 50KB получают 0 баллов
 */
function calculateFileSizeScore(
  fileSize: number,
  width: number,
  height: number
): number {
  const MIN_FILE_SIZE = 50 * 1024; // 50KB - минимальный размер для ARCore

  // Жесткое ограничение: файлы меньше 50KB не подходят для ARCore
  if (fileSize < MIN_FILE_SIZE) {
    return 0; // Критическое ограничение
  }

  const sizeInKB = fileSize / 1024;

  // Вычисляем ожидаемый размер для несжатого изображения (примерно)
  // Примерно 3 байта на пиксель для RGB
  const expectedSize = (width * height * 3) / 1024; // в KB

  // Коэффициент сжатия (чем ближе к 1, тем меньше сжатие)
  const compressionRatio = sizeInKB / expectedSize;

  // Оценка на основе коэффициента сжатия
  // Файлы от 50KB до 200KB обычно хорошо работают
  if (compressionRatio < 0.05) return 5; // Очень сильное сжатие (но файл >= 50KB)
  if (compressionRatio < 0.1) return 10; // Сильное сжатие
  if (compressionRatio < 0.3) return 15; // Умеренное сжатие
  if (compressionRatio <= 1.0) return 20; // Хорошее качество
  return 18; // Возможно несжатое (PNG), но работает
}

/**
 * Вычисляет оценку на основе соотношения сторон
 */
function calculateAspectRatioScore(width: number, height: number): number {
  const ratio = width / height;

  // Квадратные и близкие к квадрату изображения лучше для ARCore
  if (ratio >= 0.9 && ratio <= 1.1) return 20; // Квадрат
  if (ratio >= 0.7 && ratio <= 1.4) return 18; // Близко к квадрату
  if (ratio >= 0.5 && ratio <= 2.0) return 15; // Приемлемо
  return 10; // Слишком вытянутое
}

/**
 * Вычисляет оценку на основе формата файла
 */
function calculateFormatScore(format: string): number {
  const formatLower = format.toLowerCase();

  if (formatLower === "jpg" || formatLower === "jpeg") return 20; // JPEG оптимален
  if (formatLower === "png") return 15; // PNG хорош, но больше размер
  return 10; // Другие форматы
}

/**
 * Вычисляет соотношение сторон в читаемом формате
 */
function calculateAspectRatio(width: number, height: number): string {
  const gcd = (a: number, b: number): number => (b === 0 ? a : gcd(b, a % b));
  const divisor = gcd(width, height);
  const w = width / divisor;
  const h = height / divisor;

  // Упрощаем для читаемости
  if (w <= 20 && h <= 20) {
    return `${w}:${h}`;
  }

  // Если числа слишком большие, показываем десятичное
  const ratio = (width / height).toFixed(2);
  return ratio;
}

/**
 * Определяет формат изображения по имени файла
 */
function getImageFormat(filename: string): string {
  const extension = filename.split(".").pop()?.toLowerCase() || "";
  if (extension === "jpg" || extension === "jpeg") return "jpg";
  if (extension === "png") return "png";
  return extension;
}

/**
 * Форматирует размер файла в читаемый вид
 */
function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 B";

  const k = 1024;
  const sizes = ["B", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));

  return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + " " + sizes[i];
}
