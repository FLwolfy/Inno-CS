package ecs.engine.base;

import javafx.animation.AnimationTimer;
import javafx.animation.KeyFrame;
import javafx.animation.Timeline;
import javafx.scene.Scene;
import javafx.scene.layout.Pane;
import javafx.scene.layout.StackPane;
import javafx.scene.paint.Paint;
import javafx.stage.Stage;
import javafx.util.Duration;

/**
 * The main class for the game.
 * This class is responsible for setting up the game and starting the game loop.
 */
public class Game {
  ////////////// Game Constants //////////////
  public static final int WIDTH = 800;
  public static final int HEIGHT = 600;
  public static final String TITLE = "ECS Example";
  public static final Paint DEFAULT_BACKGROUND = Paint.valueOf("#202020");
  public static final double MAX_FRAME_RATE = 144.0;
  public static final double FIXED_TIME_STEP = 0.02;

  ///////////////////////////////////////////

  // Stage for the game
  private final Stage stage;

  // Time tracking for the game loop
  private static final double TIME_PER_FRAME = 1.0 / MAX_FRAME_RATE;
  private long lastLogicUpdateTime = System.nanoTime();
  private long lastFixedUpdateTime = System.nanoTime();

  /**
   * Create a new game
   * @param stage The stage (application window) to use for the game
   */
  public Game(Stage stage) {
    this.stage = stage;

    // Create the scene
    Scene scene = new Scene(new StackPane(), WIDTH, HEIGHT);
    scene.setFill(DEFAULT_BACKGROUND);
    setupCanvases(scene);
    GameScene.setInnerScene(scene);

    // Set the stage
    stage.setTitle(TITLE);
    stage.setScene(scene);
    stage.show();
  }

  private void setupCanvases(Scene scene) {
    // Create the canvas groups
    Pane uiCanvas = new Pane();
    Pane backgroundCanvas = new Pane();
    Pane gameCanvas = new Pane();

    // Add the canvases to the scene
    StackPane root = (StackPane) scene.getRoot();
    root.getChildren().addAll(backgroundCanvas, gameCanvas, uiCanvas);
  }

  private void startGameLoop() {
    lastLogicUpdateTime = System.nanoTime();
    lastFixedUpdateTime = System.nanoTime();

    // Start the fixed loop
    Timeline fixedLoop = new Timeline();
    fixedLoop.setCycleCount(Timeline.INDEFINITE);
    fixedLoop.getKeyFrames().add(new KeyFrame(
        Duration.seconds(FIXED_TIME_STEP),
        event -> GameScene.fixedStep(FIXED_TIME_STEP)
    ));
    fixedLoop.play();

    // Start the regular loop
    AnimationTimer gameLoop = new AnimationTimer() {
      @Override
      public void handle(long now) {
        double elapsedTime = (now - lastLogicUpdateTime) / 1_000_000_000.0;

        // Max Frame Rate Check
        if (elapsedTime < TIME_PER_FRAME) { return; }
        lastLogicUpdateTime = now;

        // Updates
        GameScene.step(elapsedTime);
        GameScene.renderStep();
      }
    };
    gameLoop.start();
  }

  /* API HERE */

  /**
   * Start the game
   */
  public void start() {
    // start the stage
    stage.show();

    // Start the game loop
    startGameLoop();
  }

  /**
   * Add a game scene to the game
   * @param sceneClass The class of the scene to add
   * @param <T> The type of the scene to add
   */
  public <T extends GameScene> void addGameScene(Class<T> sceneClass) {
    GameScene.addScene(sceneClass);
  }

  /**
   * Set the start scene of the game
   * @param sceneClass The class of the scene to set as the start scene
   * @param <T> The type of the scene to set as the start scene
   */
  public <T extends GameScene> void setStartScene(Class<T> sceneClass) {
    GameScene.setActiveScene(sceneClass);
  }
}
