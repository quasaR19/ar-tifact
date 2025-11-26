"use client";

import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Environment, OrbitControls, useGLTF } from "@react-three/drei";
import { Canvas, useThree } from "@react-three/fiber";
import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import * as THREE from "three";

interface GLBModelProps {
  url: string;
  showDetails?: boolean;
  centerModel?: boolean;
  onBoxCenterChange?: (center: THREE.Vector3 | null) => void;
}

function GLBModel({
  url,
  showDetails = false,
  centerModel = true,
  onBoxCenterChange,
}: GLBModelProps) {
  const { scene } = useGLTF(url, true);
  const { scene: threeScene } = useThree();
  const boxHelperRef = useRef<THREE.BoxHelper | null>(null);
  const axesHelperRef = useRef<THREE.AxesHelper | null>(null);
  const originalPivotRef = useRef<THREE.Vector3 | null>(null);
  const originalLocalPosRef = useRef<THREE.Vector3 | null>(null);
  const [boxCenter, setBoxCenter] = useState<THREE.Vector3 | null>(null);

  // Автоматически центрируем модель
  useEffect(() => {
    if (scene) {
      try {
        // Сохраняем исходную локальную позицию (для возврата при отключении центрирования)
        if (!originalLocalPosRef.current) {
          originalLocalPosRef.current = scene.position.clone();
        }

        // Сохраняем исходную позицию pivot в мировых координатах (для AxesHelper)
        if (!originalPivotRef.current) {
          scene.updateMatrixWorld(true);
          const worldPosition = new THREE.Vector3();
          scene.getWorldPosition(worldPosition);
          originalPivotRef.current = worldPosition.clone();
        }

        // Вычисляем границы модели (в локальных координатах родителя, но нам нужен размер)
        const box = new THREE.Box3().setFromObject(scene);
        const size = box.getSize(new THREE.Vector3());

        // Применяем или отменяем центрирование
        if (centerModel) {
          // Проверяем, что размеры валидны
          if (size.x > 0 || size.y > 0 || size.z > 0) {
            // Центрируем модель: смещаем так, чтобы центр стал в (0,0,0)
            // Смещение = -center
            // Но center вычислен в текущей позиции.
            // Если модель уже смещена, box.getCenter вернет уже смещенный центр (близкий к 0).
            // Нам нужно вычислить центр относительно модели, не зависящий от текущей позиции сцены.

            // Сбрасываем позицию временно для вычисления "чистого" центра
            scene.position.set(0, 0, 0);
            scene.updateMatrixWorld(true);
            const rawBox = new THREE.Box3().setFromObject(scene);
            const rawCenter = rawBox.getCenter(new THREE.Vector3());

            // Применяем смещение
            scene.position.x = -rawCenter.x;
            scene.position.y = -rawCenter.y;
            scene.position.z = -rawCenter.z;

            // Масштабируем, если модель слишком большая (только один раз? или всегда?)
            // Логика масштабирования была:
            const maxDim = Math.max(size.x, size.y, size.z);
            if (maxDim > 5) {
              const scale = 5 / maxDim;
              scene.scale.set(scale, scale, scale);
            }
          }
        } else {
          // Возвращаем на исходную позицию
          if (originalLocalPosRef.current) {
            scene.position.copy(originalLocalPosRef.current);
          }
          // Масштаб? Лучше оставить как есть или сбросить?
          // Обычно масштаб тоже часть "центрирования/нормализации".
          // Но пользователь просил только про "центрировать координаты".
          // Оставим масштаб как был вычислен или не трогаем, если он не менялся.
          // Если мы масштабировали при centerModel=true, то при переключении на false
          // масштаб останется. Это может быть странно.
          // Но логика масштабирования была "если модель слишком большая".
          // Давайте пока оставим масштаб как есть (он применяется к scene).
        }

        // Вычисляем итоговый центр для камеры
        scene.updateMatrixWorld(true);
        const finalBox = new THREE.Box3().setFromObject(scene);
        const finalCenter = finalBox.getCenter(new THREE.Vector3());
        setBoxCenter(finalCenter);
        onBoxCenterChange?.(finalCenter);

        // Обновляем BoxHelper если он существует
        if (boxHelperRef.current) {
          boxHelperRef.current.update();
        }
      } catch (error) {
        console.warn("Ошибка при центрировании модели:", error);
      }
    }
  }, [scene, centerModel, onBoxCenterChange]);

  // Управление визуализацией границ и pivot
  useEffect(() => {
    if (!scene || !threeScene) return;

    if (showDetails) {
      // Создаем BoxHelper для границ модели
      if (!boxHelperRef.current) {
        boxHelperRef.current = new THREE.BoxHelper(
          scene as THREE.Object3D,
          0x00ffff
        );
        threeScene.add(boxHelperRef.current);
        boxHelperRef.current.update();
        // Вычисляем центр BoxHelper в мировых координатах
        const boxHelperBox = new THREE.Box3().setFromObject(
          boxHelperRef.current
        );
        const boxHelperCenter = boxHelperBox.getCenter(new THREE.Vector3());
        setBoxCenter(boxHelperCenter);
        onBoxCenterChange?.(boxHelperCenter);
      } else {
        boxHelperRef.current.visible = true;
        // Обновляем BoxHelper при изменении модели
        boxHelperRef.current.update();
        // Вычисляем центр BoxHelper в мировых координатах
        const boxHelperBox = new THREE.Box3().setFromObject(
          boxHelperRef.current
        );
        const boxHelperCenter = boxHelperBox.getCenter(new THREE.Vector3());
        setBoxCenter(boxHelperCenter);
        onBoxCenterChange?.(boxHelperCenter);
      }

      // Создаем AxesHelper для pivot точки
      if (!axesHelperRef.current) {
        axesHelperRef.current = new THREE.AxesHelper(1.5);
        threeScene.add(axesHelperRef.current);
      }

      axesHelperRef.current.visible = true;

      if (centerModel) {
        // Если центрируем модель, то новый центр (и pivot) в (0,0,0)
        axesHelperRef.current.position.set(0, 0, 0);
      } else {
        // Если не центрируем, оси должны показывать исходный pivot
        if (originalPivotRef.current) {
          axesHelperRef.current.position.copy(originalPivotRef.current);
        }
      }
    } else {
      // Скрываем хелперы
      if (boxHelperRef.current) {
        boxHelperRef.current.visible = false;
      }
      if (axesHelperRef.current) {
        axesHelperRef.current.visible = false;
      }
    }

    // Очистка при размонтировании
    return () => {
      if (boxHelperRef.current) {
        threeScene.remove(boxHelperRef.current);
        boxHelperRef.current.dispose();
        boxHelperRef.current = null;
      }
      if (axesHelperRef.current) {
        threeScene.remove(axesHelperRef.current);
        axesHelperRef.current.dispose();
        axesHelperRef.current = null;
      }
    };
  }, [showDetails, scene, threeScene, onBoxCenterChange, centerModel]);

  return (
    <>
      <primitive object={scene} />
      {/* Визуализация центра модели (центр BoxHelper) */}
      {showDetails && boxCenter && (
        <mesh position={[boxCenter.x, boxCenter.y, boxCenter.z]}>
          <sphereGeometry args={[0.15, 16, 16]} />
          <meshBasicMaterial color="yellow" />
        </mesh>
      )}
    </>
  );
}

interface GLBViewerProps {
  url: string;
  className?: string;
  centerModel?: boolean;
  onCenterModelChange?: (center: boolean) => void;
}

function OrbitControlsWithTarget({ target }: { target: THREE.Vector3 | null }) {
  const { camera } = useThree();
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const controlsRef = useRef<any>(null);

  useEffect(() => {
    if (target) {
      // Настраиваем камеру и контролы на центр модели при первой загрузке
      if (controlsRef.current) {
        controlsRef.current.target.set(target.x, target.y, target.z);
        controlsRef.current.update();
      }
      // Также настраиваем камеру, чтобы она смотрела на target
      camera.lookAt(target.x, target.y, target.z);
    }
  }, [target, camera]);

  return (
    <OrbitControls
      ref={controlsRef}
      enableZoom={true}
      enablePan={true}
      enableRotate={true}
      minDistance={1}
      maxDistance={20}
      target={target ? [target.x, target.y, target.z] : [0, 0, 0]}
    />
  );
}

export function GLBViewer({
  url,
  className,
  centerModel: initialCenterModel = true,
  onCenterModelChange,
}: GLBViewerProps) {
  const [error, setError] = useState<string | null>(null);
  const [showDetails, setShowDetails] = useState(false);
  const [centerModel, setCenterModel] = useState(initialCenterModel);
  const [boxCenter, setBoxCenter] = useState<THREE.Vector3 | null>(null);

  useEffect(() => {
    if (initialCenterModel !== undefined) {
      setCenterModel(initialCenterModel);
    }
  }, [initialCenterModel]);

  const handleBoxCenterChange = useCallback((center: THREE.Vector3 | null) => {
    setBoxCenter(center);
  }, []);

  const handleCenterModelChange = (checked: boolean) => {
    setCenterModel(checked);
    onCenterModelChange?.(checked);
  };

  useEffect(() => {
    setError(null);
    // Проверяем валидность URL
    if (
      url &&
      !url.startsWith("http") &&
      !url.startsWith("blob:") &&
      !url.startsWith("/")
    ) {
      setError("Некорректный URL для загрузки модели");
    }
  }, [url]);

  if (error) {
    return (
      <div
        className={className}
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          flexDirection: "column",
          gap: "8px",
          padding: "16px",
        }}
      >
        <div className="text-4xl">⚠️</div>
        <p className="text-sm text-muted-foreground text-center">
          Ошибка загрузки модели
        </p>
        <p className="text-xs text-muted-foreground text-center">{error}</p>
      </div>
    );
  }

  return (
    <div
      className={className}
      style={{ width: "100%", height: "100%", position: "relative" }}
    >
      {/* Чекбоксы для деталей и центрирования */}
      <div
        style={{
          position: "absolute",
          top: "16px",
          left: "16px",
          zIndex: 10,
          display: "flex",
          flexDirection: "column",
          gap: "8px",
        }}
      >
        <div
          style={{
            backgroundColor: "var(--background)",
            padding: "8px 12px",
            borderRadius: "8px",
            display: "flex",
            alignItems: "center",
            gap: "8px",
          }}
        >
          <Checkbox
            id="show-details"
            checked={showDetails}
            onCheckedChange={(checked) => setShowDetails(checked === true)}
          />
          <Label
            htmlFor="show-details"
            style={{
              cursor: "pointer",
              fontSize: "14px",
              userSelect: "none",
            }}
          >
            Показать детали
          </Label>
        </div>

        <div
          style={{
            backgroundColor: "var(--background)",
            padding: "8px 12px",
            borderRadius: "8px",
            display: "flex",
            alignItems: "center",
            gap: "8px",
          }}
        >
          <Checkbox
            id="center-model"
            checked={centerModel}
            onCheckedChange={(checked) =>
              handleCenterModelChange(checked === true)
            }
          />
          <Label
            htmlFor="center-model"
            style={{
              cursor: "pointer",
              fontSize: "14px",
              userSelect: "none",
            }}
          >
            Центрировать координаты
          </Label>
        </div>
      </div>

      <Canvas
        camera={{ position: [0, 0, 5], fov: 50 }}
        gl={{ antialias: true }}
        onError={(error) => {
          console.error("GLB Viewer error:", error);
          const errorMessage =
            error instanceof Error ? error.message : String(error);
          // Проверяем, связана ли ошибка с blob URL
          if (
            errorMessage.includes("blob:") ||
            errorMessage.includes("Failed to fetch")
          ) {
            setError(
              "Ошибка загрузки файла. Попробуйте перезагрузить страницу или загрузить файл заново."
            );
          } else {
            setError("Не удалось загрузить 3D модель");
          }
        }}
      >
        {url &&
        (url.startsWith("http") ||
          url.startsWith("blob:") ||
          url.startsWith("/")) ? (
          <Suspense
            fallback={
              <mesh>
                <boxGeometry args={[1, 1, 1]} />
                <meshStandardMaterial color="orange" />
              </mesh>
            }
          >
            <ambientLight intensity={0.5} />
            <directionalLight position={[10, 10, 5]} intensity={1} />
            <pointLight position={[-10, -10, -5]} intensity={0.5} />
            <Environment preset="sunset" />
            <GLBModel
              url={url}
              showDetails={showDetails}
              centerModel={centerModel}
              onBoxCenterChange={handleBoxCenterChange}
            />
            <OrbitControlsWithTarget target={boxCenter} />
          </Suspense>
        ) : (
          <mesh>
            <boxGeometry args={[1, 1, 1]} />
            <meshStandardMaterial color="gray" />
          </mesh>
        )}
      </Canvas>
    </div>
  );
}
