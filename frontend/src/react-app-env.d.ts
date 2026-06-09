/// <reference types="react-scripts" />

// Для CSS-модулей
declare module '*.module.css' {
  const classes: { [key: string]: string };
  export default classes;
}

// Для обычных CSS-файлов (side-effect imports)
declare module '*.css';