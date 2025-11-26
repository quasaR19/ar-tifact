import { checkImageQuality, ImageQualityResult } from './imageQualityChecker';
import { cropImageToSquare, createFileFromCropped } from './imageCropper';

// Элементы DOM
const uploadArea = document.getElementById('uploadArea') as HTMLElement;
const fileInput = document.getElementById('fileInput') as HTMLInputElement;
const previewSection = document.getElementById('previewSection') as HTMLElement;
const imagePreview = document.getElementById('imagePreview') as HTMLImageElement;
const loading = document.getElementById('loading') as HTMLElement;
const errorMessage = document.getElementById('errorMessage') as HTMLElement;
const qualityScore = document.getElementById('qualityScore') as HTMLElement;
const qualityBadge = document.getElementById('qualityBadge') as HTMLElement;
const resolution = document.getElementById('resolution') as HTMLElement;
const fileSize = document.getElementById('fileSize') as HTMLElement;
const format = document.getElementById('format') as HTMLElement;
const aspectRatio = document.getElementById('aspectRatio') as HTMLElement;
const recommendationsList = document.getElementById('recommendationsList') as HTMLElement;
const requirementsStatus = document.getElementById('requirementsStatus') as HTMLElement;
const fileSizeStatus = document.getElementById('fileSizeStatus') as HTMLElement;
const croppedImageSection = document.getElementById('croppedImageSection') as HTMLElement;
const croppedImagePreview = document.getElementById('croppedImagePreview') as HTMLImageElement;
const downloadCroppedBtn = document.getElementById('downloadCroppedBtn') as HTMLButtonElement;

/**
 * Обрабатывает загруженный файл
 */
async function handleFile(file: File): Promise<void> {
  // Проверяем тип файла
  if (!file.type.startsWith('image/')) {
    showError('Пожалуйста, выберите файл изображения');
    return;
  }

  // Скрываем ошибки и показываем загрузку
  hideError();
  showLoading();
  hidePreview();

  try {
    // Проверяем качество изображения
    const result: ImageQualityResult = await checkImageQuality(file);

    // Показываем превью
    const reader = new FileReader();
    reader.onload = async (e) => {
      if (e.target?.result) {
        imagePreview.src = e.target.result as string;
        showPreview();
        displayQualityResult(result);

        // Обрезаем изображение до формата 1:1
        try {
          await handleImageCrop(file);
        } catch (cropError) {
          console.error('Ошибка при обрезке изображения:', cropError);
          hideCroppedImage();
        }
      }
    };
    reader.readAsDataURL(file);
  } catch (error) {
    showError(error instanceof Error ? error.message : 'Произошла ошибка при проверке изображения');
    hidePreview();
    hideCroppedImage();
  } finally {
    hideLoading();
  }
}

/**
 * Отображает результат проверки качества
 */
function displayQualityResult(result: ImageQualityResult): void {
  qualityScore.textContent = result.score.toString();
  qualityBadge.textContent = result.qualityLabel;
  qualityBadge.className = `quality-badge ${result.qualityClass}`;
  
  resolution.textContent = result.resolution;
  fileSize.textContent = result.fileSize;
  format.textContent = result.format;
  aspectRatio.textContent = result.aspectRatio;
  
  // Отображаем статус соответствия минимальным требованиям по разрешению
  if (requirementsStatus) {
    if (result.meetsMinimumRequirements) {
      requirementsStatus.textContent = '✓ Разрешение соответствует требованиям ARCore (300x300)';
      requirementsStatus.className = 'requirements-status met';
    } else {
      requirementsStatus.textContent = '✗ Разрешение не соответствует требованиям ARCore (минимум 300x300)';
      requirementsStatus.className = 'requirements-status not-met';
    }
  }

  // Отображаем статус соответствия требованиям по размеру файла
  if (fileSizeStatus) {
    if (result.meetsFileSizeRequirement) {
      fileSizeStatus.textContent = '✓ Размер файла соответствует требованиям ARCore (минимум 50 KB)';
      fileSizeStatus.className = 'requirements-status met';
    } else {
      fileSizeStatus.textContent = '✗ Размер файла не соответствует требованиям ARCore (минимум 50 KB)';
      fileSizeStatus.className = 'requirements-status not-met';
    }
  }
  
  // Отображаем рекомендации
  if (recommendationsList) {
    recommendationsList.innerHTML = '';
    if (result.recommendations.length > 0) {
      const title = document.createElement('div');
      title.className = 'recommendations-title';
      title.textContent = 'Рекомендации:';
      recommendationsList.appendChild(title);
      
      result.recommendations.forEach(rec => {
        const item = document.createElement('div');
        item.className = 'recommendation-item';
        item.textContent = `• ${rec}`;
        recommendationsList.appendChild(item);
      });
    } else {
      const item = document.createElement('div');
      item.className = 'recommendation-item success';
      item.textContent = '✓ Изображение соответствует всем требованиям ARCore';
      recommendationsList.appendChild(item);
    }
  }
}

/**
 * Показывает/скрывает элементы
 */
function showLoading(): void {
  loading.classList.add('active');
}

function hideLoading(): void {
  loading.classList.remove('active');
}

function showPreview(): void {
  previewSection.classList.add('active');
}

function hidePreview(): void {
  previewSection.classList.remove('active');
}

function showError(message: string): void {
  errorMessage.textContent = message;
  errorMessage.classList.add('active');
}

function hideError(): void {
  errorMessage.classList.remove('active');
}

/**
 * Обрабатывает обрезку изображения до формата 1:1
 */
async function handleImageCrop(file: File): Promise<void> {
  try {
    const croppedResult = await cropImageToSquare(file);
    
    // Отображаем обрезанное изображение
    croppedImagePreview.src = croppedResult.dataUrl;
    showCroppedImage();

    // Сохраняем данные для скачивания
    downloadCroppedBtn.onclick = () => {
      const croppedFile = createFileFromCropped(croppedResult, file.name);
      downloadFile(croppedFile);
    };
  } catch (error) {
    console.error('Ошибка обрезки:', error);
    hideCroppedImage();
  }
}

/**
 * Скачивает файл
 */
function downloadFile(file: File): void {
  const url = URL.createObjectURL(file);
  const a = document.createElement('a');
  a.href = url;
  a.download = file.name;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function showCroppedImage(): void {
  if (croppedImageSection) {
    croppedImageSection.classList.add('active');
  }
}

function hideCroppedImage(): void {
  if (croppedImageSection) {
    croppedImageSection.classList.remove('active');
  }
}

/**
 * Обработчики событий
 */

// Клик по области загрузки
uploadArea.addEventListener('click', () => {
  fileInput.click();
});

// Выбор файла через input
fileInput.addEventListener('change', (e) => {
  const target = e.target as HTMLInputElement;
  if (target.files && target.files.length > 0) {
    handleFile(target.files[0]);
  }
});

// Drag and Drop
uploadArea.addEventListener('dragover', (e) => {
  e.preventDefault();
  uploadArea.classList.add('dragover');
});

uploadArea.addEventListener('dragleave', () => {
  uploadArea.classList.remove('dragover');
});

uploadArea.addEventListener('drop', (e) => {
  e.preventDefault();
  uploadArea.classList.remove('dragover');
  
  const files = e.dataTransfer?.files;
  if (files && files.length > 0) {
    handleFile(files[0]);
  }
});

