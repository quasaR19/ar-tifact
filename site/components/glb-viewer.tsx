"use client";

import { Canvas } from "@react-three/fiber";
import { OrbitControls, useGLTF, Environment } from "@react-three/drei";
import { Suspense, useState, useEffect } from "react";
import * as THREE from "three";

interface GLBModelProps {
  url: string;
}

function GLBModel({ url }: GLBModelProps) {
  const { scene } = useGLTF(url, true);
  
  // Автоматически центрируем модель
  useEffect(() => {
    if (scene) {
      try {
        // Вычисляем границы модели
        const box = new THREE.Box3().setFromObject(scene);
        const center = box.getCenter(new THREE.Vector3());
        const size = box.getSize(new THREE.Vector3());
        
        // Проверяем, что размеры валидны
        if (size.x > 0 || size.y > 0 || size.z > 0) {
          // Центрируем модель
          scene.position.x = -center.x;
          scene.position.y = -center.y;
          scene.position.z = -center.z;
          
          // Масштабируем, если модель слишком большая
          const maxDim = Math.max(size.x, size.y, size.z);
          if (maxDim > 5) {
            const scale = 5 / maxDim;
            scene.scale.set(scale, scale, scale);
          }
        }
      } catch (error) {
        console.warn("Ошибка при центрировании модели:", error);
      }
    }
  }, [scene]);
  
  return <primitive object={scene} />;
}

interface GLBViewerProps {
  url: string;
  className?: string;
}

export function GLBViewer({ url, className }: GLBViewerProps) {
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setError(null);
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
    <div className={className} style={{ width: "100%", height: "100%" }}>
      <Canvas
        camera={{ position: [0, 0, 5], fov: 50 }}
        gl={{ antialias: true }}
        onError={(error) => {
          console.error("GLB Viewer error:", error);
          setError("Не удалось загрузить 3D модель");
        }}
      >
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
          <GLBModel url={url} />
          <OrbitControls
            enableZoom={true}
            enablePan={true}
            enableRotate={true}
            minDistance={1}
            maxDistance={20}
          />
        </Suspense>
      </Canvas>
    </div>
  );
}

